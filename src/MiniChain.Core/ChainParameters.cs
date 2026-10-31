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

    /// <summary>The largest allowed <see cref="BlockReward"/>. It keeps every balance far from overflowing a long.</summary>
    public const long MaxBlockReward = 1_000_000_000_000;

    /// <summary>New coins a miner may pay itself in each block's coinbase, on top of the fees.</summary>
    public long BlockReward
    {
        get;
        init => field = value is > 0 and <= MaxBlockReward
            ? value
            : throw new ArgumentOutOfRangeException(nameof(BlockReward), value, $"Must be between 1 and {MaxBlockReward}.");
    } = 50;

    /// <summary>
    /// How far ahead of a node's clock a block's timestamp may be (two hours, as in Bitcoin). Clocks on
    /// different machines never agree exactly, so a little drift has to be allowed.
    /// </summary>
    public long MaxFutureBlockTimeMs
    {
        get;
        init => field = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxFutureBlockTimeMs), value, "Cannot be negative.");
    } = 2 * 60 * 60 * 1000;

    /// <summary>How many transactions a block may hold besides its coinbase.</summary>
    public int MaxTransactionsPerBlock
    {
        get;
        init => field = value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxTransactionsPerBlock), value, "Cannot be negative.");
    } = 100;

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
