using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace MiniChain.Node;

public enum PeerAddResult { Added, AlreadyKnown, Invalid, Self }

/// <summary>The URLs of the other nodes this node talks to. Thread-safe.</summary>
public sealed class PeerRegistry
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _peers = new(StringComparer.Ordinal);
    private readonly NodeIdentity _identity;

    public PeerRegistry(NodeIdentity identity, IOptions<NodeOptions> options)
    {
        _identity = identity;
        foreach (var peer in options.Value.Peers)
        {
            if (Add(peer, out _) == PeerAddResult.Invalid)
                throw new InvalidOperationException($"MiniChain:Peers contains '{peer}', which is not an absolute http(s) URL.");
        }
    }

    /// <summary>The known peers, never including this node itself.</summary>
    public IReadOnlyList<string> GetPeers()
    {
        var self = _identity.Url;
        lock (_gate)
            return _peers.Where(peer => peer != self).Order(StringComparer.Ordinal).ToArray();
    }

    public PeerAddResult Add(string? url, out string? normalized)
    {
        if (!TryNormalize(url, out normalized))
            return PeerAddResult.Invalid;
        if (normalized == _identity.Url)
            return PeerAddResult.Self;

        lock (_gate)
            return _peers.Add(normalized) ? PeerAddResult.Added : PeerAddResult.AlreadyKnown;
    }

    public bool Remove(string? url)
    {
        if (!TryNormalize(url, out var normalized))
            return false;

        lock (_gate)
            return _peers.Remove(normalized);
    }

    /// <summary>
    /// Reduces a peer URL to <c>scheme://host:port</c>, so that "http://LocalHost:5002/" and
    /// "http://localhost:5002" count as the same peer. Only plain http(s) base URLs are accepted.
    /// </summary>
    public static bool TryNormalize(string? url, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
