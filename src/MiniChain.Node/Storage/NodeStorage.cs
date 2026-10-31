using System.Globalization;
using MiniChain.Core;

namespace MiniChain.Node.Storage;

/// <summary>The folder where a node keeps its chain and its wallet, or null when nothing is persisted.</summary>
public sealed record NodeDataDirectory(string? Path);

public static class NodeStorage
{
    /// <summary>
    /// Resolves <see cref="NodeOptions.DataDirectory"/> against the content root. Without a configured folder
    /// each node gets <c>node-data/&lt;port&gt;</c>, so several nodes started from the same folder (as in
    /// <c>dotnet run --urls http://localhost:5001</c>, <c>...:5002</c>, ...) never share files.
    /// </summary>
    public static NodeDataDirectory ResolveDataDirectory(NodeOptions options, IConfiguration configuration, IHostEnvironment environment)
    {
        if (string.Equals(options.Storage, "memory", StringComparison.OrdinalIgnoreCase))
            return new NodeDataDirectory(null);

        var directory = options.DataDirectory ?? System.IO.Path.Combine("node-data", PortFrom(configuration));
        return new NodeDataDirectory(System.IO.Path.GetFullPath(directory, environment.ContentRootPath));
    }

    public static IChainStore CreateStore(NodeOptions options, NodeDataDirectory dataDirectory) =>
        options.Storage.ToUpperInvariant() switch
        {
            "MEMORY" => new InMemoryChainStore(),
            "JSON" => new JsonChainStore(System.IO.Path.Combine(dataDirectory.Path!, "chain.json")),
            "SQLITE" => new SqliteChainStore(System.IO.Path.Combine(dataDirectory.Path!, "chain.db")),
            _ => throw new InvalidOperationException($"MiniChain:Storage must be 'json', 'sqlite' or 'memory', not '{options.Storage}'."),
        };

    /// <summary>
    /// Loads the stored chain and revalidates every block from the genesis block on. A file that was edited,
    /// damaged or written with other chain settings stops the node instead of being trusted.
    /// </summary>
    public static Blockchain LoadChain(IChainStore store, ChainParameters parameters)
    {
        var stored = store.Load();
        if (stored is null)
            return new Blockchain(parameters);

        return ChainValidator.TryValidate(stored, parameters, out var validated, out var error)
            ? new Blockchain(validated)
            : throw new InvalidDataException(
                $"The chain stored in {store.Location} is corrupted or was created with different settings: {error} " +
                "Move the file away to start from an empty chain and sync from peers.");
    }

    private static string PortFrom(IConfiguration configuration)
    {
        // --urls and ASPNETCORE_URLS end up in "urls"; ASPNETCORE_HTTP_PORTS ends up in "http_ports".
        var url = configuration["urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (url is not null)
        {
            url = url.Replace("://+", "://localhost", StringComparison.Ordinal).Replace("://*", "://localhost", StringComparison.Ordinal);
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Port.ToString(CultureInfo.InvariantCulture);
        }

        return configuration["http_ports"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? "default";
    }
}
