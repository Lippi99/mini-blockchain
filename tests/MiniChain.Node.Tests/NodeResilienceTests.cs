using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniChain.Core;
using MiniChain.Node.Storage;

namespace MiniChain.Node.Tests;

/// <summary>Regressions from the node code review, and the sync paths that had no tests.</summary>
public sealed class NodeResilienceTests : IAsyncLifetime
{
    private readonly TestNetwork _network = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _network.DisposeAsync();

    private static async Task<HttpResponseMessage> SendBlockFromAsync(TestNetwork.TestNode node, Block block, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/blocks") { Content = JsonContent.Create(block, options: MiniChainJson.Options) };
        if (origin is not null)
            request.Headers.Add(HttpPeerClient.OriginHeader, origin);
        return await node.Client.SendAsync(request);
    }

    /// <summary>Two nodes where <paramref name="behind"/> knows <paramref name="ahead"/>, but not the other way round, so nothing is gossiped.</summary>
    private (TestNetwork.TestNode Ahead, TestNetwork.TestNode Behind) StartOneWayPair(IReadOnlyDictionary<string, string?>? behindSettings = null)
    {
        var ahead = _network.StartNode("ahead");
        var behind = _network.StartNode("behind", behindSettings);
        behind.AddPeerDirectly(ahead);
        return (ahead, behind);
    }

    [Fact]
    public async Task A_dead_peer_does_not_delay_gossip_to_healthy_peers()
    {
        var node = _network.StartNode("node1", new Dictionary<string, string?> { ["MiniChain:PeerTimeoutSeconds"] = "60" });
        var healthy = _network.StartNode("healthy");
        _network.AddBlackHole("dead");
        await node.AddPeerAsync(healthy);
        using var added = await node.Client.PostAsJsonAsync("/peers", new PeerRequest("http://dead"), MiniChainJson.Options);
        added.EnsureSuccessStatusCode();

        // The first block may still reach the healthy peer through the sync it runs when it learns about
        // this node. After that only gossip can deliver blocks, and every message to "dead" hangs for 60 s.
        await node.MineAsync();
        await Wait.UntilAsync(async () => (await healthy.GetStatusAsync()).Height == 1, "the healthy peer has the first block", timeoutSeconds: 15);
        for (var i = 0; i < 3; i++)
            await node.MineAsync();

        await Wait.UntilAsync(async () => (await healthy.GetStatusAsync()).Height == 4, "the healthy peer has all blocks", timeoutSeconds: 15);
    }

    [Fact]
    public async Task A_removed_peer_gets_no_more_gossip_but_the_others_do()
    {
        var node = _network.StartNode("node1");
        var kept = _network.StartNode("kept");
        var removed = _network.StartNode("removed");
        node.AddPeerDirectly(kept);
        node.AddPeerDirectly(removed);

        await node.MineAsync();
        await Wait.UntilInSyncAsync(node, kept, removed); // Both peers really do receive gossip at first.

        await node.RemovePeerAsync(removed);
        await node.MineAsync();

        await Wait.UntilInSyncAsync(node, kept);
        Assert.Equal(1, (await removed.GetStatusAsync()).Height);
    }

    [Fact]
    public async Task A_queued_sync_with_a_peer_that_is_not_known_is_skipped()
    {
        var source = _network.StartNode("source");
        var node = _network.StartNode("node1");
        await source.MineAsync();
        node.AddPeerDirectly(source);
        var sync = node.Services.GetRequiredService<ChainSyncService>();

        sync.RequestSync("http://removed");
        sync.RequestSync(source.Url);

        // Syncs run one at a time, so once this later one is done the earlier request has been handled too.
        await Wait.UntilInSyncAsync(source, node);
        Assert.DoesNotContain("removed", _network.RequestedHosts);
    }

    [Fact]
    public async Task A_block_that_is_ahead_makes_the_node_sync_from_a_known_origin()
    {
        var (ahead, behind) = StartOneWayPair();
        Block tip = null!;
        for (var i = 0; i < 3; i++)
            tip = await ahead.MineAsync();

        using var response = await SendBlockFromAsync(behind, tip, ahead.Url);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("syncing", (await response.Content.ReadFromJsonAsync<BlockReceivedResponse>(MiniChainJson.Options))!.Status);
        await Wait.UntilInSyncAsync(ahead, behind);
    }

    [Fact]
    public async Task A_block_that_is_ahead_without_an_origin_makes_the_node_sync_from_its_peers()
    {
        var (ahead, behind) = StartOneWayPair();
        Block tip = null!;
        for (var i = 0; i < 3; i++)
            tip = await ahead.MineAsync();

        using var response = await SendBlockFromAsync(behind, tip, origin: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Wait.UntilInSyncAsync(ahead, behind);
    }

    [Fact]
    public async Task An_unknown_origin_never_makes_the_node_fetch_from_that_url()
    {
        var source = _network.StartNode("source");
        var node = _network.StartNode("node1");
        Block tip = null!;
        for (var i = 0; i < 3; i++)
            tip = await source.MineAsync();

        using var response = await SendBlockFromAsync(node, tip, "http://attacker");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Syncs run one at a time, so once this later one is done the earlier request has been handled too.
        await node.AddPeerAsync(source);
        await Wait.UntilInSyncAsync(source, node);

        Assert.DoesNotContain("attacker", _network.RequestedHosts);
    }

    [Fact]
    public async Task The_periodic_poll_catches_up_when_gossip_never_arrives()
    {
        var (ahead, behind) = StartOneWayPair(new Dictionary<string, string?> { ["MiniChain:SyncIntervalSeconds"] = "1" });

        await ahead.MineAsync();
        await Wait.UntilInSyncAsync(ahead, behind);
        await ahead.MineAsync();
        await Wait.UntilInSyncAsync(ahead, behind);
    }

    [Fact]
    public async Task Adding_a_peer_that_is_already_known_still_syncs()
    {
        var (ahead, behind) = StartOneWayPair();
        await ahead.MineAsync();
        await ahead.MineAsync();
        Assert.Equal(0, (await behind.GetStatusAsync()).Height);

        await behind.AddPeerAsync(ahead);

        await Wait.UntilInSyncAsync(ahead, behind);
    }

    [Fact]
    public async Task Simultaneous_mining_requests_take_turns()
    {
        var node = _network.StartNode("node1");

        var blocks = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => node.MineAsync()));

        Assert.Equal([1, 2, 3, 4, 5], blocks.Select(block => block.Index).Order());
        Assert.Equal(5, (await node.GetStatusAsync()).Height);
    }

    [Fact]
    public async Task A_chain_that_cannot_win_is_not_even_validated()
    {
        var node = _network.StartNode("node1");
        await node.MineAsync();
        await node.MineAsync();
        var other = TwoBlocksWithAnInvalidReward();

        var result = node.Services.GetRequiredService<NodeState>().TryAdoptChain(other);

        Assert.Equal(ChainAdoption.NotBetter, result.Status);
    }

    [Fact]
    public async Task A_chain_that_claims_more_work_is_fully_validated()
    {
        var node = _network.StartNode("node1");
        await node.MineAsync();
        var other = TwoBlocksWithAnInvalidReward();

        var result = node.Services.GetRequiredService<NodeState>().TryAdoptChain(other);

        Assert.Equal(ChainAdoption.Invalid, result.Status);
        Assert.Contains("the coinbase pays 1000", result.Error);
    }

    [Fact]
    public void A_chain_dated_hours_ahead_is_not_adopted()
    {
        var node = _network.StartNode("node1");
        using var miner = new Wallet();
        var chain = new Blockchain(new ChainParameters { Difficulty = 1 });
        chain.MinePendingTransactions(new Mempool(), miner.Address, DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeMilliseconds());

        var result = node.Services.GetRequiredService<NodeState>().TryAdoptChain(chain.Blocks);

        Assert.Equal(ChainAdoption.Invalid, result.Status);
        Assert.Contains("in the future", result.Error);
    }

    [Fact]
    public async Task A_failing_store_does_not_stop_the_node()
    {
        var node = _network.StartNode("node1", replaceServices: services => services.AddSingleton<IChainStore, FailingStore>());

        var block = await node.MineAsync();

        Assert.Equal(1, block.Index);
        Assert.Equal(1, (await node.GetStatusAsync()).Height);
    }

    private static List<Block> TwoBlocksWithAnInvalidReward()
    {
        using var miner = new Wallet();
        var chain = new Blockchain(new ChainParameters { Difficulty = 1 });
        for (var i = 0; i < 2; i++)
            chain.MinePendingTransactions(new Mempool(), miner.Address, Genesis.Timestamp + i);

        var block = chain.Blocks[1];
        var coinbase = block.Transactions[0];
        block.Transactions = [Transaction.CreateCoinbase(coinbase.To, 1000, coinbase.Nonce, coinbase.Timestamp)];
        Miner.MineSequential(block);
        return [.. chain.Blocks];
    }

    private sealed class FailingStore : IChainStore
    {
        public string Location => "nowhere";

        public IReadOnlyList<Block>? Load() => null;

        public void Save(IReadOnlyList<Block> blocks) => throw new IOException("The disk is full.");
    }
}
