using System.Net.Http.Json;
using MiniChain.Core;

namespace MiniChain.Node;

/// <summary>How this node talks to other nodes. An interface so tests can see what a node sends.</summary>
public interface IPeerClient
{
    Task<IReadOnlyList<Block>> GetChainAsync(string peer, CancellationToken cancellationToken);

    Task<Block> GetLatestBlockAsync(string peer, CancellationToken cancellationToken);

    Task SendBlockAsync(string peer, Block block, CancellationToken cancellationToken);

    Task SendTransactionAsync(string peer, Transaction transaction, CancellationToken cancellationToken);

    /// <summary>Asks <paramref name="peer"/> to add this node to its peers.</summary>
    Task IntroduceAsync(string peer, CancellationToken cancellationToken);
}

/// <summary>Calls the other nodes' HTTP APIs.</summary>
public sealed class HttpPeerClient(IHttpClientFactory httpClientFactory, NodeIdentity identity) : IPeerClient
{
    /// <summary>The named <see cref="HttpClient"/> used for every request to a peer.</summary>
    public const string HttpClientName = "peers";

    /// <summary>Tells the receiving node who sent a block or transaction, so it knows whom to sync from.</summary>
    public const string OriginHeader = "X-MiniChain-Peer";

    public async Task<IReadOnlyList<Block>> GetChainAsync(string peer, CancellationToken cancellationToken)
    {
        var chain = await Client().GetFromJsonAsync<ChainResponse>($"{peer}/chain", MiniChainJson.Options, cancellationToken);
        return chain?.Blocks ?? throw new InvalidDataException($"{peer} returned an empty chain.");
    }

    public async Task<Block> GetLatestBlockAsync(string peer, CancellationToken cancellationToken) =>
        await Client().GetFromJsonAsync<Block>($"{peer}/chain/latest", MiniChainJson.Options, cancellationToken)
        ?? throw new InvalidDataException($"{peer} returned no block.");

    public Task SendBlockAsync(string peer, Block block, CancellationToken cancellationToken) =>
        PostAsync($"{peer}/blocks", block, cancellationToken);

    public Task SendTransactionAsync(string peer, Transaction transaction, CancellationToken cancellationToken) =>
        PostAsync($"{peer}/transactions", transaction, cancellationToken);

    public Task IntroduceAsync(string peer, CancellationToken cancellationToken) =>
        identity.Url is { } self
            ? PostAsync($"{peer}/peers", new PeerRequest(self), cancellationToken)
            : Task.CompletedTask;

    private async Task PostAsync<T>(string url, T body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, options: MiniChainJson.Options),
        };
        if (identity.Url is { } self)
            request.Headers.Add(OriginHeader, self);

        // A 4xx answer ("already known", "invalid") is the peer's normal reply to gossip, not a failure to deliver it.
        using var response = await Client().SendAsync(request, cancellationToken);
        if ((int)response.StatusCode >= 500)
            throw new HttpRequestException($"{url} answered {(int)response.StatusCode}.", null, response.StatusCode);
    }

    private HttpClient Client() => httpClientFactory.CreateClient(HttpClientName);
}
