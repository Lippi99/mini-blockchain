using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MiniChain.Core;

namespace MiniChain.Node.Tests;

/// <summary>
/// Runs MiniChain nodes in memory. Each node is a <see cref="WebApplicationFactory{TEntryPoint}"/> with its
/// own TestServer, and the nodes' peer HttpClients use <see cref="RoutingHandler"/>, which delivers a request
/// for http://node2/... to the server of the node called "node2". The nodes therefore talk to each other
/// through their real HTTP API without opening any ports.
/// </summary>
public sealed class TestNetwork : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, HttpMessageHandler> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TestNode> _nodes = [];

    public TestNode StartNode(string name, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["MiniChain:PublicUrl"] = $"http://{name}",
            ["MiniChain:Difficulty"] = "1",
            ["MiniChain:MiningThreads"] = "2",
            ["MiniChain:SyncIntervalSeconds"] = "0",
            ["MiniChain:Storage"] = "memory",
            ["Logging:LogLevel:Default"] = "Warning",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            configuration[key] = value;

        var factory = new NodeFactory(configuration, () => new RoutingHandler(_servers));
        try
        {
            _servers[name] = factory.Server.CreateHandler(); // Starts the node.
        }
        catch
        {
            factory.Dispose();
            throw;
        }

        var node = new TestNode(name, factory);
        _nodes.Add(node);
        return node;
    }

    /// <summary>Makes every node a peer of every other node.</summary>
    public async Task ConnectAllAsync()
    {
        foreach (var node in _nodes)
        {
            foreach (var other in _nodes.Where(other => other != node))
                await node.AddPeerAsync(other);
        }
    }

    public async Task StopNodeAsync(TestNode node)
    {
        _servers.TryRemove(node.Name, out _);
        _nodes.Remove(node);
        await node.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Unplug every node first, so nodes that are still gossiping get "unreachable" instead of a disposed server.
        _servers.Clear();
        foreach (var node in _nodes)
            await node.DisposeAsync();
        _nodes.Clear();
    }

    private sealed class NodeFactory(IReadOnlyDictionary<string, string?> settings, Func<HttpMessageHandler> peerHandler)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(HttpPeerClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(peerHandler));
        }
    }

    /// <summary>Delivers each request to the in-memory server named by the URL's host.</summary>
    private sealed class RoutingHandler(ConcurrentDictionary<string, HttpMessageHandler> servers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            if (!servers.TryGetValue(host, out var server))
                throw new HttpRequestException($"No node called '{host}' is running.");

            // The servers' handlers belong to the network, so the invoker must not dispose them.
            using var invoker = new HttpMessageInvoker(server, disposeHandler: false);
            return invoker.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>One running node, with helpers for its HTTP API.</summary>
    public sealed class TestNode : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;

        internal TestNode(string name, WebApplicationFactory<Program> factory)
        {
            Name = name;
            _factory = factory;
            Client = factory.CreateClient();
        }

        public string Name { get; }

        public string Url => $"http://{Name}";

        public HttpClient Client { get; }

        public IServiceProvider Services => _factory.Services;

        public async Task<StatusResponse> GetStatusAsync() =>
            (await Client.GetFromJsonAsync<StatusResponse>("/", MiniChainJson.Options))!;

        public async Task<ChainResponse> GetChainAsync() =>
            (await Client.GetFromJsonAsync<ChainResponse>("/chain", MiniChainJson.Options))!;

        public async Task<AccountResponse> GetAccountAsync(string address) =>
            (await Client.GetFromJsonAsync<AccountResponse>($"/balance/{address}", MiniChainJson.Options))!;

        public async Task<IReadOnlyList<Transaction>> GetMempoolAsync() =>
            (await Client.GetFromJsonAsync<List<Transaction>>("/mempool", MiniChainJson.Options))!;

        public async Task<IReadOnlyList<string>> GetPeersAsync() =>
            (await Client.GetFromJsonAsync<List<string>>("/peers", MiniChainJson.Options))!;

        public async Task<Block> MineAsync(string? minerAddress = null)
        {
            using var response = await Client.PostAsync(minerAddress is null ? "/mine" : $"/mine?minerAddress={minerAddress}", null);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<Block>(MiniChainJson.Options))!;
        }

        public Task<HttpResponseMessage> SubmitTransactionAsync(Transaction transaction) =>
            Client.PostAsJsonAsync("/transactions", transaction, MiniChainJson.Options);

        public Task<HttpResponseMessage> SubmitBlockAsync(Block block) =>
            Client.PostAsJsonAsync("/blocks", block, MiniChainJson.Options);

        public async Task AddPeerAsync(TestNode other)
        {
            using var response = await Client.PostAsJsonAsync("/peers", new PeerRequest(other.Url), MiniChainJson.Options);
            response.EnsureSuccessStatusCode();
        }

        public async Task RemovePeerAsync(TestNode other)
        {
            using var response = await Client.DeleteAsync($"/peers?url={Uri.EscapeDataString(other.Url)}");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }
    }
}

internal static class Wait
{
    /// <summary>Polls <paramref name="condition"/> until it holds, failing the test after <paramref name="timeoutSeconds"/>.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, string description, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out after {timeoutSeconds} s waiting until {description}.");
            await Task.Delay(50);
        }
    }

    public static Task UntilInSyncAsync(params TestNetwork.TestNode[] nodes) =>
        UntilAsync(async () =>
        {
            var statuses = await Task.WhenAll(nodes.Select(node => node.GetStatusAsync()));
            return statuses.Select(status => status.LatestHash).Distinct().Count() == 1;
        }, $"{string.Join(", ", nodes.Select(node => node.Name))} share the same tip");
}
