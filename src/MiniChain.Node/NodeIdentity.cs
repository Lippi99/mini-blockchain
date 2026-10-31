using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using MiniChain.Core;

namespace MiniChain.Node;

/// <summary>Who this node is: the URL peers reach it at, and the address its mining rewards go to.</summary>
public sealed class NodeIdentity : IDisposable
{
    private readonly IServer _server;
    private readonly string? _configuredUrl;
    private readonly Wallet? _wallet;

    public NodeIdentity(IOptions<NodeOptions> options, IServer server)
    {
        _server = server;
        var settings = options.Value;

        if (settings.PublicUrl is not null)
        {
            _configuredUrl = PeerRegistry.TryNormalize(settings.PublicUrl, out var url)
                ? url
                : throw new InvalidOperationException($"MiniChain:PublicUrl '{settings.PublicUrl}' is not an absolute http(s) URL.");
        }

        if (settings.MinerAddress is not null)
        {
            MinerAddress = Address.IsValid(settings.MinerAddress)
                ? settings.MinerAddress
                : throw new InvalidOperationException($"MiniChain:MinerAddress '{settings.MinerAddress}' is not a valid address.");
        }
        else
        {
            // Without a configured address the node mines to a wallet of its own. Phase 9 keeps it on disk.
            _wallet = new Wallet();
            MinerAddress = _wallet.Address;
        }
    }

    /// <summary>The URL other nodes use to reach this one, or null while it is not known yet.</summary>
    public string? Url => _configuredUrl ?? UrlFromServer();

    public string MinerAddress { get; }

    public void Dispose() => _wallet?.Dispose();

    private string? UrlFromServer()
    {
        var address = _server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (address is null)
            return null;

        // Kestrel reports wildcard hosts such as http://[::]:5001; other nodes on this machine can use localhost.
        address = address.Replace("://+", "://localhost", StringComparison.Ordinal)
            .Replace("://*", "://localhost", StringComparison.Ordinal)
            .Replace("://[::]", "://localhost", StringComparison.Ordinal)
            .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal);
        return PeerRegistry.TryNormalize(address, out var url) ? url : null;
    }
}
