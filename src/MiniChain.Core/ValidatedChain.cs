using System.Numerics;

namespace MiniChain.Core;

/// <summary>
/// A chain that passed full validation from the genesis block, together with the ledger state its
/// transactions produce and its cumulative work. Created by <see cref="ChainValidator.TryValidate"/>,
/// so the expensive validation can happen before (and outside of any lock around) the cheap
/// decision of whether to adopt it.
/// </summary>
public sealed class ValidatedChain
{
    internal ValidatedChain(IReadOnlyList<Block> blocks, LedgerState ledger, ChainParameters parameters)
    {
        Blocks = blocks;
        Ledger = ledger;
        Parameters = parameters;
        CumulativeWork = blocks.Aggregate(BigInteger.Zero, (work, block) => work + ProofOfWork.Work(block.Difficulty));
    }

    public IReadOnlyList<Block> Blocks { get; }

    public ChainParameters Parameters { get; }

    /// <summary>The sum of 16^difficulty over all blocks. With a fixed difficulty, more work simply means a longer chain.</summary>
    public BigInteger CumulativeWork { get; }

    internal LedgerState Ledger { get; }
}
