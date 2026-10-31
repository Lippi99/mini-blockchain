using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using MiniChain.Core;
using MiniChain.Node.Storage;

namespace MiniChain.Node;

/// <summary>Who this node is: the URL peers reach it at, and the address its mining rewards go to.</summary>
public sealed class NodeIdentity : IDisposable
{
    private readonly IServer _server;
    private readonly string? _configuredUrl;
    private readonly Wallet? _wallet;

    public NodeIdentity(IOptions<NodeOptions> options, IServer server, NodeDataDirectory dataDirectory)
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
            return;
        }

        // Without a configured address the node mines to a wallet of its own. It is kept in the data folder
        // (which Git ignores), so its rewards can still be spent after a restart, for example with
        // "dotnet run --project src/MiniChain.Cli -- send --wallet-dir <data folder> --name node-wallet ...".
        if (dataDirectory.Path is { } folder)
        {
            WalletPath = Path.Combine(folder, "node-wallet.pem");
            if (File.Exists(WalletPath))
            {
                _wallet = Wallet.Load(WalletPath);
            }
            else
            {
                _wallet = new Wallet();
                _wallet.Save(WalletPath);
            }
        }
        else
        {
            _wallet = new Wallet();
        }
        MinerAddress = _wallet.Address;
    }

    /// <summary>The URL other nodes use to reach this one, or null while it is not known yet.</summary>
    public string? Url => _configuredUrl ?? UrlFromServer();

    public string MinerAddress { get; }

    /// <summary>The key file of the node's own wallet, if it has one on disk.</summary>
    public string? WalletPath { get; }

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
