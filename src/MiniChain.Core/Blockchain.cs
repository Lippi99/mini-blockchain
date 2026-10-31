using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>An ordered list of blocks, each one linked to the previous block by its hash.</summary>
public sealed class Blockchain(ChainParameters? parameters = null)
{
    private readonly List<Block> _blocks = [Genesis.Create()];

    public ChainParameters Parameters { get; } = parameters ?? ChainParameters.Default;

    public IReadOnlyList<Block> Blocks => _blocks;

    public Block LatestBlock => _blocks[^1];

    /// <summary>The difficulty the next block must have.</summary>
    public int NextDifficulty => DifficultyAdjustment.ExpectedDifficulty(_blocks, _blocks.Count, Parameters);

    /// <summary>Re-checks every block; <paramref name="error"/> says which block is broken and why.</summary>
    public bool IsValid([NotNullWhen(false)] out string? error) => ChainValidator.ValidateChain(_blocks, Parameters, out error);

    /// <summary>Links <paramref name="block"/> to the current tip, mines it and appends it.</summary>
    public void AddBlock(Block block)
    {
        block.Index = LatestBlock.Index + 1;
        block.PreviousHash = LatestBlock.Hash;
        block.Difficulty = NextDifficulty;
        Miner.MineParallel(block);
        _blocks.Add(block);
    }
}
