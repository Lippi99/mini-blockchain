namespace MiniChain.Core.Tests;

public class BlockchainTests
{
    [Fact]
    public void A_new_chain_starts_with_the_genesis_block()
    {
        var chain = new Blockchain();

        var genesis = Assert.Single(chain.Blocks);
        Assert.Equal(0, genesis.Index);
        Assert.Equal("0", genesis.PreviousHash);
        Assert.Equal(Genesis.Timestamp, genesis.Timestamp);
        Assert.Empty(genesis.Transactions);
        Assert.Equal(genesis.ComputeHash(), genesis.Hash);
        Assert.Same(genesis, chain.LatestBlock);
    }

    [Fact]
    public void The_genesis_block_is_identical_on_every_node()
    {
        // "0|1767225600000|0|{SHA-256 of nothing}|0|0", hashed with `sha256sum`.
        const string expected = "da44ee9dbf97ea70a2708d7b53349c502804187bf169078737fee3ef83246235";

        Assert.Equal(expected, Genesis.Hash);
        Assert.Equal(expected, new Blockchain().Blocks[0].Hash);
        Assert.Equal(expected, new Blockchain(new ChainParameters { Difficulty = 6 }).Blocks[0].Hash);
    }

    [Fact]
    public void Each_chain_gets_its_own_copy_of_the_genesis_block()
    {
        var first = new Blockchain();
        var second = new Blockchain();

        first.Blocks[0].Nonce = 99;

        Assert.NotSame(first.Blocks[0], second.Blocks[0]);
        Assert.Equal(0, second.Blocks[0].Nonce);
    }

    [Fact]
    public void AddBlock_links_the_block_to_the_previous_one()
    {
        var chain = TestChains.Create(blocks: 3);

        Assert.Equal(4, chain.Blocks.Count);
        for (var i = 1; i < chain.Blocks.Count; i++)
        {
            var block = chain.Blocks[i];
            Assert.Equal(i, block.Index);
            Assert.Equal(chain.Blocks[i - 1].Hash, block.PreviousHash);
            Assert.Equal(block.ComputeHash(), block.Hash);
        }
        Assert.Same(chain.Blocks[^1], chain.LatestBlock);
    }

    [Fact]
    public void AddBlock_mines_the_block_at_the_network_difficulty()
    {
        var chain = TestChains.Create(blocks: 2, difficulty: 2);

        Assert.All(chain.Blocks.Skip(1), block =>
        {
            Assert.Equal(2, block.Difficulty);
            Assert.StartsWith("00", block.Hash);
        });
    }
}
