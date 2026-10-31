using MiniChain.Core;

namespace MiniChain.Node.Storage;

/// <summary>Keeps a node's chain between restarts.</summary>
public interface IChainStore
{
    /// <summary>Where the chain is kept, for log messages.</summary>
    string Location { get; }

    /// <summary>
    /// The stored blocks, or null if nothing was stored yet. Throws <see cref="InvalidDataException"/> if the
    /// data cannot be read. The blocks are not validated here; the caller revalidates the whole chain.
    /// </summary>
    IReadOnlyList<Block>? Load();

    /// <summary>Makes the stored chain equal to <paramref name="blocks"/>.</summary>
    void Save(IReadOnlyList<Block> blocks);
}

/// <summary>Stores nothing: the chain lives only as long as the process. Used by tests and throwaway nodes.</summary>
public sealed class InMemoryChainStore : IChainStore
{
    public string Location => "memory (not persisted)";

    public IReadOnlyList<Block>? Load() => null;

    public void Save(IReadOnlyList<Block> blocks)
    {
    }
}
