namespace MiniChain.Core.Tests;

public class ChainValidationTests
{
    [Fact]
    public void An_untouched_chain_of_mined_blocks_is_valid()
    {
        var chain = TestChains.Create(blocks: 3);

        Assert.True(chain.IsValid(out var error));
        Assert.Null(error);
    }

    [Fact]
    public void A_chain_with_only_the_genesis_block_is_valid()
    {
        Assert.True(new Blockchain().IsValid(out _));
    }

    [Fact]
    public void Rule1_tampered_contents_are_detected_at_the_tampered_block()
    {
        var chain = TestChains.Create(blocks: 3);

        chain.Blocks[1].Transactions = ["payment 1000"];

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #1:", error);
        Assert.Contains("does not match the block's contents", error);
    }

    [Fact]
    public void Recomputing_the_hash_of_a_tampered_block_fails_proof_of_work()
    {
        var chain = TestChains.Create(blocks: 3);

        chain.Blocks[1].Transactions = ["payment 1000"];
        TestChains.HashWithoutMining(chain.Blocks[1]);

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #1:", error);
        Assert.Contains("never mined", error);
    }

    [Fact]
    public void Remining_a_tampered_block_breaks_the_next_block_instead()
    {
        var chain = TestChains.Create(blocks: 3);

        chain.Blocks[1].Transactions = ["payment 1000"];
        Miner.MineSequential(chain.Blocks[1]);

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("previous hash", error);
    }

    [Fact]
    public void Rule2_a_broken_link_is_detected()
    {
        var chain = TestChains.Create(blocks: 3);
        var block = chain.Blocks[2];

        block.PreviousHash = new string('f', 64);
        Miner.MineSequential(block); // Keep the hash and proof of work valid so only the link is wrong.

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("does not match the hash of block #1", error);
    }

    [Fact]
    public void Rule3_a_wrong_index_is_detected()
    {
        var chain = TestChains.Create(blocks: 3);
        var block = chain.Blocks[2];

        block.Index = 7;
        Miner.MineSequential(block);

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("index is 7, expected 2", error);
    }

    [Fact]
    public void Rule4_a_timestamp_earlier_than_the_previous_block_is_detected()
    {
        var chain = TestChains.Create(blocks: 3);
        var block = chain.Blocks[2];

        block.Timestamp = chain.Blocks[1].Timestamp - 1;
        Miner.MineSequential(block);

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("earlier than the previous block's timestamp", error);
    }

    [Fact]
    public void Rule4_an_equal_timestamp_is_allowed()
    {
        var chain = TestChains.Create();
        chain.AddBlock(new Block { Timestamp = Genesis.Timestamp, Transactions = ["same millisecond"] });

        Assert.True(chain.IsValid(out _));
    }

    [Fact]
    public void A_block_that_was_never_mined_fails_validation()
    {
        var chain = TestChains.Create(blocks: 2, difficulty: 2);
        var block = TestChains.NextBlock(chain.Blocks, difficulty: 2, "unmined");
        TestChains.HashWithoutMining(block);

        Assert.False(ChainValidator.ValidateChain([.. chain.Blocks, block], chain.Parameters, out var error));
        Assert.StartsWith("Block #3:", error);
        Assert.Contains("never mined", error);
    }

    [Fact]
    public void A_block_mined_below_the_network_difficulty_is_rejected()
    {
        var chain = TestChains.Create(blocks: 2, difficulty: 2);
        var cheap = TestChains.NextBlock(chain.Blocks, difficulty: 1, "cheap block");
        Miner.MineSequential(cheap);

        Assert.False(ChainValidator.ValidateChain([.. chain.Blocks, cheap], chain.Parameters, out var error));
        Assert.Equal("Block #3: the difficulty is 1, but the network expects 2.", error);
    }

    [Fact]
    public void A_modified_genesis_block_is_rejected_even_with_a_recomputed_hash()
    {
        var chain = TestChains.Create(blocks: 3);
        var genesis = chain.Blocks[0];

        genesis.Transactions = ["premine 1000000 to Mallory"];
        genesis.Hash = genesis.ComputeHash();

        Assert.False(chain.IsValid(out var error));
        Assert.Equal("Block #0 is not the expected genesis block.", error);
    }

    [Fact]
    public void An_empty_chain_is_invalid()
    {
        Assert.False(ChainValidator.ValidateChain([], ChainParameters.Default, out var error));
        Assert.Equal("The chain is empty.", error);
    }
}
