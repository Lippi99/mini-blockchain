namespace MiniChain.Core;

/// <summary>
/// The hard-coded first block. Every node must start from exactly this block, or they will never
/// agree on a chain.
/// </summary>
public static class Genesis
{
    /// <summary>2026-01-01T00:00:00Z in Unix milliseconds.</summary>
    public const long Timestamp = 1_767_225_600_000;

    /// <summary>The genesis block's hash, which every valid chain starts with.</summary>
    public static string Hash { get; } = Create().Hash;

    /// <summary>Returns a new copy of the genesis block. Blocks are mutable, so chains never share one.</summary>
    public static Block Create()
    {
        var block = new Block
        {
            Index = 0,
            Timestamp = Timestamp,
            PreviousHash = "0",
            Transactions = [],
            Difficulty = 0,
            Nonce = 0,
        };
        block.Hash = block.ComputeHash();
        return block;
    }
}
