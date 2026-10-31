namespace MiniChain.Core.Tests;

public class ChainValidationTests
{
    private static Blockchain BuildChain(int blocks = 3)
    {
        var chain = new Blockchain();
        for (var i = 1; i <= blocks; i++)
            chain.AddBlock(new Block { Timestamp = Genesis.Timestamp + i * 1000, Transactions = [$"payment {i}"] });
        return chain;
    }

    [Fact]
    public void An_untouched_chain_is_valid()
    {
        var chain = BuildChain();

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
        var chain = BuildChain();

        chain.Blocks[1].Transactions = ["payment 1000"];

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #1:", error);
        Assert.Contains("does not match the block's contents", error);
    }

    [Fact]
    public void Recomputing_a_tampered_blocks_hash_breaks_the_next_block_instead()
    {
        var chain = BuildChain();

        chain.Blocks[1].Transactions = ["payment 1000"];
        chain.Blocks[1].Hash = chain.Blocks[1].ComputeHash();

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("previous hash", error);
    }

    [Fact]
    public void Rule2_a_broken_link_is_detected()
    {
        var chain = BuildChain();
        var block = chain.Blocks[2];

        block.PreviousHash = new string('f', 64);
        block.Hash = block.ComputeHash(); // Keep rule 1 satisfied so only the link is wrong.

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("does not match the hash of block #1", error);
    }

    [Fact]
    public void Rule3_a_wrong_index_is_detected()
    {
        var chain = BuildChain();
        var block = chain.Blocks[2];

        block.Index = 7;
        block.Hash = block.ComputeHash();

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("index is 7, expected 2", error);
    }

    [Fact]
    public void Rule4_a_timestamp_earlier_than_the_previous_block_is_detected()
    {
        var chain = BuildChain();
        var block = chain.Blocks[2];

        block.Timestamp = chain.Blocks[1].Timestamp - 1;
        block.Hash = block.ComputeHash();

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2:", error);
        Assert.Contains("earlier than the previous block's timestamp", error);
    }

    [Fact]
    public void Rule4_an_equal_timestamp_is_allowed()
    {
        var chain = new Blockchain();
        chain.AddBlock(new Block { Timestamp = Genesis.Timestamp, Transactions = ["same millisecond"] });

        Assert.True(chain.IsValid(out _));
    }

    [Fact]
    public void A_modified_genesis_block_is_rejected_even_with_a_recomputed_hash()
    {
        var chain = BuildChain();
        var genesis = chain.Blocks[0];

        genesis.Transactions = ["premine 1000000 to Mallory"];
        genesis.Hash = genesis.ComputeHash();

        Assert.False(chain.IsValid(out var error));
        Assert.Equal("Block #0 is not the expected genesis block.", error);
    }

    [Fact]
    public void An_empty_chain_is_invalid()
    {
        Assert.False(ChainValidator.ValidateChain([], out var error));
        Assert.Equal("The chain is empty.", error);
    }
}
