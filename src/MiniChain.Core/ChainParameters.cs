namespace MiniChain.Core;

/// <summary>The rules every node on a network must agree on.</summary>
public sealed record ChainParameters
{
    public static ChainParameters Default { get; } = new();

    /// <summary>
    /// Leading zero hex digits a block hash needs. When <see cref="AdjustDifficulty"/> is on, this is
    /// only the starting difficulty.
    /// </summary>
    public int Difficulty
    {
        get;
        init => field = value is >= 0 and <= ProofOfWork.MaxDifficulty
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Difficulty), value, $"Must be between 0 and {ProofOfWork.MaxDifficulty}.");
    } = 4;

    /// <summary>Retarget the difficulty every <see cref="AdjustmentInterval"/> blocks. Off by default.</summary>
    public bool AdjustDifficulty { get; init; }

    /// <summary>How many blocks pass between difficulty adjustments.</summary>
    public int AdjustmentInterval
    {
        get;
        init => field = value >= 2
            ? value
            : throw new ArgumentOutOfRangeException(nameof(AdjustmentInterval), value, "Must be at least 2.");
    } = 10;

    /// <summary>The block time difficulty adjustment aims for, in milliseconds.</summary>
    public long TargetBlockTimeMs
    {
        get;
        init => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(TargetBlockTimeMs), value, "Must be positive.");
    } = 10_000;
}
