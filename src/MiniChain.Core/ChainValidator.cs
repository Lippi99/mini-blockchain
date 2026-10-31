using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>Detects tampering by re-checking every block in a chain.</summary>
public static class ChainValidator
{
    /// <summary>
    /// Validates a whole chain: it must start with the expected genesis block, and every later
    /// block must pass <see cref="ValidateBlock"/> against the block before it.
    /// </summary>
    public static bool ValidateChain(IReadOnlyList<Block> blocks, [NotNullWhen(false)] out string? error)
    {
        if (blocks.Count == 0)
        {
            error = "The chain is empty.";
            return false;
        }

        // Comparing both the stored and the recomputed hash checks every header field at once.
        if (blocks[0].Hash != Genesis.Hash || blocks[0].ComputeHash() != Genesis.Hash)
        {
            error = "Block #0 is not the expected genesis block.";
            return false;
        }

        for (var i = 1; i < blocks.Count; i++)
        {
            if (!ValidateBlock(blocks[i], blocks[i - 1], out var blockError))
            {
                error = $"Block #{i}: {blockError}";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Checks that <paramref name="block"/> is intact and correctly linked to <paramref name="previous"/>.</summary>
    public static bool ValidateBlock(Block block, Block previous, [NotNullWhen(false)] out string? error)
    {
        var computedHash = block.ComputeHash();
        if (block.Hash != computedHash)
            error = $"the stored hash {block.Hash} does not match the block's contents (computed {computedHash}).";
        else if (block.PreviousHash != previous.Hash)
            error = $"the previous hash {block.PreviousHash} does not match the hash of block #{previous.Index} ({previous.Hash}).";
        else if (block.Index != previous.Index + 1)
            error = $"the index is {block.Index}, expected {previous.Index + 1}.";
        else if (block.Timestamp < previous.Timestamp)
            error = $"the timestamp {block.Timestamp} is earlier than the previous block's timestamp {previous.Timestamp}.";
        else
            error = null;

        return error is null;
    }
}
