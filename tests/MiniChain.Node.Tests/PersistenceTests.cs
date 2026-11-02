using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MiniChain.Core;
using MiniChain.Node.Storage;

namespace MiniChain.Node.Tests;

/// <summary>Phase 9: a node keeps its chain between restarts and refuses to start from a corrupted one.</summary>
public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly TestNetwork _network = new();
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "minichain-tests", Guid.NewGuid().ToString("N"));
    private readonly Wallet _alice = new();
    private readonly Wallet _bob = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _network.DisposeAsync();
        _alice.Dispose();
        _bob.Dispose();
        if (Directory.Exists(_dataDirectory))
            Directory.Delete(_dataDirectory, recursive: true);
    }

    private Dictionary<string, string?> Settings(string storage) => new()
    {
        ["MiniChain:Storage"] = storage,
        ["MiniChain:DataDirectory"] = _dataDirectory,
    };

    private async Task MineAndStopAsync(string storage, int blocks)
    {
        var node = _network.StartNode("node1", Settings(storage));
        for (var i = 0; i < blocks; i++)
            await node.MineAsync(_alice.Address);
        await _network.StopNodeAsync(node);
    }

    private static string AllMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
                messages.AddRange(aggregate.InnerExceptions.Select(AllMessages));
        }
        return string.Join(" | ", messages);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("sqlite")]
    public async Task A_node_restarts_without_losing_its_chain(string storage)
    {
        var node = _network.StartNode("node1", Settings(storage));
        await node.MineAsync(_alice.Address);
        using var payment = await node.SubmitTransactionAsync(
            _alice.CreateTransaction(_bob.Address, 10, fee: 1, nonce: 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        Assert.Equal(HttpStatusCode.Accepted, payment.StatusCode);
        await node.MineAsync();
        await node.MineAsync();
        var before = await node.GetStatusAsync();
        await _network.StopNodeAsync(node);

        var restarted = _network.StartNode("node1", Settings(storage));

        var after = await restarted.GetStatusAsync();
        Assert.Equal(3, after.Height);
        Assert.Equal(before.LatestHash, after.LatestHash);
        Assert.Equal(before.MinerAddress, after.MinerAddress); // The node's own wallet was kept too.
        Assert.Equal(10, (await restarted.GetAccountAsync(_bob.Address)).Balance);
        Assert.Equal(2, (await restarted.GetAccountAsync(_alice.Address)).NextNonce);
        Assert.Equal(4, (await restarted.MineAsync()).Index);
    }

    [Fact]
    public async Task Peers_survive_a_restart_and_removed_ones_stay_removed()
    {
        var node = _network.StartNode("node1", Settings("json"));
        using (var kept = await node.Client.PostAsJsonAsync("/peers", new PeerRequest("http://kept"), MiniChainJson.Options))
            kept.EnsureSuccessStatusCode();
        using (var dropped = await node.Client.PostAsJsonAsync("/peers", new PeerRequest("http://dropped"), MiniChainJson.Options))
            dropped.EnsureSuccessStatusCode();
        using (var removed = await node.Client.DeleteAsync("/peers?url=http%3A%2F%2Fdropped"))
            removed.EnsureSuccessStatusCode();
        await _network.StopNodeAsync(node);

        var restarted = _network.StartNode("node1", Settings("json"));

        Assert.Equal(["http://kept"], await restarted.GetPeersAsync());
    }

    [Fact]
    public async Task A_damaged_peers_file_does_not_stop_the_node()
    {
        Directory.CreateDirectory(_dataDirectory);
        await File.WriteAllTextAsync(Path.Combine(_dataDirectory, "peers.json"), "{ not a list");

        var node = _network.StartNode("node1", Settings("json"));

        Assert.Empty(await node.GetPeersAsync());
    }

    [Fact]
    public async Task An_edited_chain_file_stops_the_node_at_startup()
    {
        await MineAndStopAsync("json", blocks: 2);
        var file = Path.Combine(_dataDirectory, "chain.json");
        var json = await File.ReadAllTextAsync(file);
        Assert.Contains("\"amount\": 50,", json);

        await File.WriteAllTextAsync(file, json.Replace("\"amount\": 50,", "\"amount\": 5000,", StringComparison.Ordinal));

        var error = Assert.ThrowsAny<Exception>(() => _network.StartNode("node1", Settings("json")));
        Assert.Contains("is corrupted", AllMessages(error));
        Assert.Contains("Block #1:", AllMessages(error));
    }

    [Fact]
    public async Task A_chain_file_that_is_not_json_stops_the_node_at_startup()
    {
        await MineAndStopAsync("json", blocks: 1);
        await File.WriteAllTextAsync(Path.Combine(_dataDirectory, "chain.json"), "{ this is not a chain");

        var error = Assert.ThrowsAny<Exception>(() => _network.StartNode("node1", Settings("json")));
        Assert.Contains("is not a valid chain file", AllMessages(error));
    }

    [Fact]
    public async Task An_edited_database_stops_the_node_at_startup()
    {
        await MineAndStopAsync("sqlite", blocks: 2);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_dataDirectory, "chain.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE transactions SET amount = 5000 WHERE block_idx = 2";
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var error = Assert.ThrowsAny<Exception>(() => _network.StartNode("node1", Settings("sqlite")));
        Assert.Contains("is corrupted", AllMessages(error));
        Assert.Contains("Block #2:", AllMessages(error));
    }

    [Fact]
    public async Task The_sqlite_store_follows_a_switch_to_another_chain()
    {
        var path = Path.Combine(_dataDirectory, "fork.db");
        var store = new SqliteChainStore(path);
        var ours = new Blockchain(new ChainParameters { Difficulty = 1 });
        for (var i = 0; i < 3; i++)
            ours.MinePendingTransactions(new Mempool(), _alice.Address, Genesis.Timestamp + i);
        store.Save(ours.Blocks);

        var theirs = new Blockchain(ours.Parameters);
        Assert.True(theirs.TryAddBlock(ours.Blocks[1], out _));
        for (var i = 0; i < 3; i++)
            theirs.MinePendingTransactions(new Mempool(), _bob.Address, Genesis.Timestamp + 100 + i);
        store.Save(theirs.Blocks);

        var loaded = store.Load()!;
        Assert.Equal(theirs.Blocks.Select(b => b.Hash), loaded.Select(b => b.Hash));
        Assert.True(ChainValidator.ValidateChain(loaded, ours.Parameters, out var error), error);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("http://localhost:5002", null, "5002")]
    [InlineData("http://+:5003;https://+:7003", null, "5003")]
    [InlineData(null, "8080", "8080")]
    [InlineData(null, null, "default")]
    public void Each_node_gets_its_own_data_folder_by_default(string? urls, string? httpPorts, string folder)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["urls"] = urls, ["http_ports"] = httpPorts })
            .Build();

        var directory = NodeStorage.ResolveDataDirectory(new NodeOptions(), configuration, new FakeEnvironment(_dataDirectory));

        Assert.Equal(Path.Combine(_dataDirectory, "node-data", folder), directory.Path);
    }

    private sealed class FakeEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "MiniChain.Node";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
