using System.Numerics;

namespace MiniChain.Core.Tests;

public class ConsensusTests
{
    private static readonly TestAccount Alice = TestAccounts.Alice;
    private static readonly TestAccount Bob = TestAccounts.Bob;
    private static readonly TestAccount Carol = TestAccounts.Carol;

    [Fact]
    public void Cumulative_work_adds_16_to_the_power_of_the_difficulty_per_block()
    {
        var chain = TestChains.Create(blocks: 3, difficulty: 2);

        Assert.Equal(1 + 3 * 256, chain.CumulativeWork);
        Assert.Equal(new BigInteger(65_536), ProofOfWork.Work(4));
    }

    [Fact]
    public void The_longer_valid_chain_wins()
    {
        var ours = TestChains.Create(blocks: 2);
        var theirs = TestChains.Create(blocks: 4);

        Assert.True(ours.TryReplaceChain(theirs.Blocks, out _, out var error), error);

        Assert.Equal(theirs.Blocks.Select(b => b.Hash), ours.Blocks.Select(b => b.Hash));
        Assert.Equal(theirs.CumulativeWork, ours.CumulativeWork);
        Assert.Equal(theirs.GetBalance(TestAccounts.Miner.Address), ours.GetBalance(TestAccounts.Miner.Address));
        Assert.True(ours.ContainsBlock(theirs.LatestBlock.Hash));
    }

    [Fact]
    public void A_shorter_or_equally_long_chain_is_not_adopted()
    {
        var ours = TestChains.Create(blocks: 3);
        var tipBefore = ours.LatestBlock.Hash;

        Assert.False(ours.TryReplaceChain(TestChains.Create(blocks: 2).Blocks, out _, out var shorter));
        Assert.False(ours.TryReplaceChain(TestChains.Create(blocks: 3).Blocks, out _, out var equal));

        Assert.Contains("is not more than this chain's", shorter);
        Assert.Contains("is not more than this chain's", equal);
        Assert.Equal(tipBefore, ours.LatestBlock.Hash);
    }

    [Fact]
    public void A_longer_but_invalid_chain_is_rejected()
    {
        var ours = TestChains.Create(blocks: 2);
        var theirs = TestChains.Create(blocks: 5);
        TestChains.ChangeReward(theirs.Blocks[3], 1_000);
        Miner.MineSequential(theirs.Blocks[3]);

        Assert.False(ours.TryReplaceChain(theirs.Blocks, out _, out var error));

        Assert.StartsWith("the chain is invalid: Block #3:", error);
        Assert.Contains("the coinbase pays 1000", error);
        Assert.Equal(3, ours.Blocks.Count);
    }

    [Fact]
    public void More_work_wins_even_with_fewer_blocks()
    {
        var parameters = new ChainParameters { Difficulty = 1, AdjustDifficulty = true, AdjustmentInterval = 2, TargetBlockTimeMs = 1000 };

        // Six slow blocks stay at difficulty 1: 1 + 6 x 16 = 97 units of work.
        var ours = new Blockchain(parameters);
        for (var i = 1; i <= 6; i++)
            TestChains.MineBlockAt(ours, Genesis.Timestamp + i * 60_000, TestAccounts.Miner);

        // Five fast blocks climb to difficulty 3: 1 + 16 + 16 + 256 + 256 + 4096 = 4641.
        var theirs = new Blockchain(parameters);
        for (var i = 1; i <= 5; i++)
            TestChains.MineBlockAt(theirs, Genesis.Timestamp + i, TestAccounts.Miner);

        Assert.True(ours.TryReplaceChain(theirs.Blocks, out _, out var error), error);
        Assert.Equal(6, ours.Blocks.Count);
        Assert.Equal(new BigInteger(4641), ours.CumulativeWork);
    }

    [Fact]
    public void Transactions_from_discarded_blocks_are_returned_unless_the_new_chain_has_them()
    {
        // Both chains share block #1, in which Alice earns 50 coins.
        var ours = TestChains.Create();
        TestChains.MineBlock(ours, Alice);
        var theirs = new Blockchain(ours.Parameters);
        Assert.True(theirs.TryAddBlock(ours.Blocks[1], out var error), error);

        var inBoth = Alice.Pay(Bob, 5, nonce: 1);
        var onlyOurs = Alice.Pay(Carol, 7, nonce: 2);
        TestChains.MineBlock(ours, Carol, inBoth, onlyOurs);

        TestChains.MineBlock(theirs, Bob, inBoth);
        TestChains.MineBlock(theirs, Bob);

        Assert.True(ours.TryReplaceChain(theirs.Blocks, out var orphaned, out error), error);

        Assert.Equal([onlyOurs], orphaned);
        var mempool = new Mempool();
        Assert.Empty(mempool.Revalidate(ours, orphaned));
        Assert.Equal([onlyOurs], mempool.Transactions);
    }

    [Fact]
    public void A_validated_chain_can_be_loaded_into_a_new_blockchain()
    {
        var original = TestChains.Create();
        TestChains.MineBlock(original, Alice);
        TestChains.MineBlock(original, Bob, Alice.Pay(Bob, 20, nonce: 1, fee: 1));

        Assert.True(ChainValidator.TryValidate(original.Blocks, original.Parameters, out var validated, out var error), error);
        var loaded = new Blockchain(validated);

        Assert.Equal(original.LatestBlock.Hash, loaded.LatestBlock.Hash);
        Assert.Equal(original.CumulativeWork, loaded.CumulativeWork);
        Assert.Equal(original.Balances, loaded.Balances);
        Assert.Equal(1, loaded.GetNonce(Alice.Address));
        Assert.True(loaded.ContainsTransaction(original.Blocks[2].Transactions[1].Id));
    }

    [Fact]
    public void A_chain_validated_with_other_parameters_is_rejected()
    {
        var ours = TestChains.Create(new ChainParameters { Difficulty = 1, BlockReward = 50 });
        var theirs = TestChains.Create(new ChainParameters { Difficulty = 1, BlockReward = 60 }, blocks: 3);
        Assert.True(ChainValidator.TryValidate(theirs.Blocks, theirs.Parameters, out var validated, out _));

        Assert.False(ours.TryReplaceChain(validated, out _, out var error));
        Assert.Contains("different chain parameters", error);
    }

    [Fact]
    public void Validation_works_on_a_copy_of_the_block_list()
    {
        var source = TestChains.Create(blocks: 2).Blocks.ToList();
        Assert.True(ChainValidator.TryValidate(source, ChainParameters.Default with { Difficulty = 1 }, out var validated, out _));

        source.RemoveAt(2);

        Assert.Equal(3, validated.Blocks.Count);
    }

    [Fact]
    public void A_malformed_genesis_block_is_rejected_without_crashing()
    {
        var genesis = Genesis.Create();
        genesis.Transactions = [null!];

        Assert.False(ChainValidator.TryValidate([genesis], ChainParameters.Default, out _, out var error));
        Assert.Equal("Block #0 is not the expected genesis block.", error);
        Assert.False(ChainValidator.TryValidate([null!], ChainParameters.Default, out _, out _));
    }
}
