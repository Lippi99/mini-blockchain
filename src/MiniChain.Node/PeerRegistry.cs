using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiniChain.Core;
using MiniChain.Node.Storage;

namespace MiniChain.Node;

public enum PeerAddResult { Added, AlreadyKnown, Invalid, Self }

/// <summary>
/// The URLs of the other nodes this node talks to, kept in the data folder so they survive a restart.
/// Thread-safe.
/// </summary>
public sealed class PeerRegistry
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _peers = new(StringComparer.Ordinal);
    private readonly NodeIdentity _identity;
    private readonly ILogger<PeerRegistry> _logger;
    private readonly string? _path;

    public PeerRegistry(NodeIdentity identity, IOptions<NodeOptions> options, NodeDataDirectory dataDirectory, ILogger<PeerRegistry> logger)
    {
        _identity = identity;
        _logger = logger;
        _path = dataDirectory.Path is { } folder ? Path.Combine(folder, "peers.json") : null;

        foreach (var peer in options.Value.Peers)
        {
            if (!TryNormalize(peer, out var normalized))
                throw new InvalidOperationException($"MiniChain:Peers contains '{peer}', which is not an absolute http(s) URL.");
            _peers.Add(normalized);
        }

        foreach (var peer in LoadSavedPeers())
        {
            if (TryNormalize(peer, out var normalized))
                _peers.Add(normalized);
        }
    }

    /// <summary>The known peers, never including this node itself.</summary>
    public IReadOnlyList<string> GetPeers()
    {
        var self = _identity.Url;
        lock (_gate)
            return _peers.Where(peer => peer != self).Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Whether <paramref name="normalizedUrl"/>, already in the form <see cref="TryNormalize"/> gives, is a known peer.</summary>
    public bool IsKnown(string normalizedUrl)
    {
        lock (_gate)
            return _peers.Contains(normalizedUrl);
    }

    public PeerAddResult Add(string? url, out string? normalized)
    {
        if (!TryNormalize(url, out normalized))
            return PeerAddResult.Invalid;
        if (normalized == _identity.Url)
            return PeerAddResult.Self;

        lock (_gate)
        {
            if (!_peers.Add(normalized))
                return PeerAddResult.AlreadyKnown;
            Save();
            return PeerAddResult.Added;
        }
    }

    public bool Remove(string? url)
    {
        if (!TryNormalize(url, out var normalized))
            return false;

        lock (_gate)
        {
            if (!_peers.Remove(normalized))
                return false;
            Save();
            return true;
        }
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

    // The peer list is a convenience, not consensus data: a damaged file only costs the saved peers.
    private List<string> LoadSavedPeers()
    {
        if (_path is null || !File.Exists(_path))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path), MiniChainJson.Options) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.PeersNotLoaded(_logger, _path, e.Message);
            return [];
        }
    }

    private void Save()
    {
        if (_path is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_peers.Order(StringComparer.Ordinal), MiniChainJson.Options));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.PeersNotSaved(_logger, _path, e.Message);
        }
    }
}
