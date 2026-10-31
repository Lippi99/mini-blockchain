namespace MiniChain.Core;

/// <summary>
/// Decides which difficulty a block must have. With adjustment off, that is always
/// <see cref="ChainParameters.Difficulty"/>. With adjustment on, the difficulty is re-evaluated every
/// <see cref="ChainParameters.AdjustmentInterval"/> blocks and changes by at most one step.
/// </summary>
public static class DifficultyAdjustment
{
    /// <summary>
    /// The difficulty required for the block at <paramref name="index"/>. Only the blocks before
    /// that index are used, so this works both for validating a stored chain and for the next block.
    /// </summary>
    public static int ExpectedDifficulty(IReadOnlyList<Block> chain, int index, ChainParameters parameters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, chain.Count);

        var interval = parameters.AdjustmentInterval;
        if (!parameters.AdjustDifficulty || index <= interval)
            return parameters.Difficulty;

        var previous = chain[index - 1];

        // Blocks 1..interval form the first period, so retargets happen at indexes interval+1, 2*interval+1, ...
        if ((index - 1) % interval != 0)
            return previous.Difficulty;

        // Measure how long the last `interval` blocks took. They span interval-1 gaps between blocks,
        // and the genesis block (with its fixed, old timestamp) is never part of the window.
        var first = chain[index - interval];
        var actualMs = previous.Timestamp - first.Timestamp;
        var targetMs = (interval - 1) * parameters.TargetBlockTimeMs;

        // One hex digit is a 16x change in work, so only react to large deviations. Without this band
        // the difficulty would flip back and forth every period.
        if (actualMs < targetMs / 2)
            return Math.Min(previous.Difficulty + 1, ProofOfWork.MaxDifficulty);
        if (actualMs > targetMs * 2)
            return Math.Max(previous.Difficulty - 1, 1);
        return previous.Difficulty;
    }
}
