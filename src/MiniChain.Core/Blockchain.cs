namespace MiniChain.Core;

/// <summary>An ordered list of blocks, each one linked to the previous block by its hash.</summary>
public sealed class Blockchain
{
    private readonly List<Block> _blocks = [Genesis.Create()];

    public IReadOnlyList<Block> Blocks => _blocks;

    public Block LatestBlock => _blocks[^1];

    /// <summary>Links <paramref name="block"/> to the current tip, computes its hash and appends it.</summary>
    public void AddBlock(Block block)
    {
        block.Index = LatestBlock.Index + 1;
        block.PreviousHash = LatestBlock.Hash;
        block.Hash = block.ComputeHash();
        _blocks.Add(block);
    }
}
