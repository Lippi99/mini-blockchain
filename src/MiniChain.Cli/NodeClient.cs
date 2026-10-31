using System.Net.Http.Json;
using System.Text.Json;
using MiniChain.Core;

namespace MiniChain.Cli;

// The parts of the node's JSON responses the CLI reads.
internal sealed record NodeStatus(string? Url, long Height, string LatestHash, int NextDifficulty, string CumulativeWork,
    int PendingTransactions, IReadOnlyList<string> Peers, string MinerAddress);

internal sealed record NodeChain(int Length, string CumulativeWork, IReadOnlyList<Block> Blocks);

internal sealed record NodeAccount(string Address, long Balance, long Nonce, long NextNonce);

internal sealed record NodeError(string Error);

/// <summary>Calls a node's HTTP API. Network and API errors become <see cref="CliUsageException"/>s with a readable message.</summary>
internal sealed class NodeClient : IDisposable
{
    private readonly HttpClient _http;

    public NodeClient(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
            throw new CliUsageException($"'{url}' is not a valid node URL.");

        // Mining at a high difficulty can take a while, so be patient.
        _http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(10) };
    }

    public string Url => _http.BaseAddress!.ToString().TrimEnd('/');

    public Task<NodeStatus> GetStatusAsync() => SendAsync<NodeStatus>(HttpMethod.Get, "/");

    public Task<NodeChain> GetChainAsync() => SendAsync<NodeChain>(HttpMethod.Get, "/chain");

    public Task<NodeAccount> GetAccountAsync(string address) => SendAsync<NodeAccount>(HttpMethod.Get, $"/balance/{address}");

    public Task<Block> MineAsync(string? minerAddress) =>
        SendAsync<Block>(HttpMethod.Post, minerAddress is null ? "/mine" : $"/mine?minerAddress={minerAddress}");

    public Task<JsonElement> SubmitTransactionAsync(Transaction transaction) =>
        SendAsync<JsonElement>(HttpMethod.Post, "/transactions", transaction);

    public Task<List<string>> GetPeersAsync() => SendAsync<List<string>>(HttpMethod.Get, "/peers");

    public Task<JsonElement> AddPeerAsync(string url) => SendAsync<JsonElement>(HttpMethod.Post, "/peers", new { url });

    public async Task RemovePeerAsync(string url) =>
        await SendAsync<JsonElement?>(HttpMethod.Delete, $"/peers?url={Uri.EscapeDataString(url)}");

    public void Dispose() => _http.Dispose();

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: MiniChainJson.Options);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException e)
        {
            throw new CliUsageException($"Could not reach the node at {Url} ({e.Message}). Is it running? Choose another one with --node.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new CliUsageException($"The node answered {(int)response.StatusCode}: {await ReadErrorAsync(response)}");

            if (response.Content.Headers.ContentLength == 0)
                return default!;
            return (await response.Content.ReadFromJsonAsync<T>(MiniChainJson.Options))!;
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        try
        {
            return JsonSerializer.Deserialize<NodeError>(text, MiniChainJson.Options)?.Error ?? text;
        }
        catch (JsonException)
        {
            return text.Length > 0 ? text : response.ReasonPhrase ?? "no details";
        }
    }
}
