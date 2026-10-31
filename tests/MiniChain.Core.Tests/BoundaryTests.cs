namespace MiniChain.Core.Tests;

/// <summary>Edge cases and boundaries of the block, ledger and mempool rules.</summary>
public class BoundaryTests
{
    private static readonly TestAccount Alice = TestAccounts.Alice;
    private static readonly TestAccount Bob = TestAccounts.Bob;
    private static readonly TestAccount Carol = TestAccounts.Carol;

    private static Blockchain ChainWhereAliceHas50(ChainParameters? parameters = null)
    {
        var chain = TestChains.Create(parameters ?? new ChainParameters { Difficulty = 1 });
        TestChains.MineBlock(chain, Alice);
        return chain;
    }

    [Fact]
    public void TryAddBlock_checks_the_header_not_just_the_transactions()
    {
        var chain = TestChains.Create(blocks: 1, difficulty: 2);

        var unmined = chain.CreateCandidateBlock(Bob.Address, [], Genesis.Timestamp + 5000);
        TestChains.HashWithoutMining(unmined);
        Assert.False(chain.TryAddBlock(unmined, out var error));
        Assert.Contains("never mined", error);

        var tooEasy = chain.CreateCandidateBlock(Bob.Address, [], Genesis.Timestamp + 5000);
        tooEasy.Difficulty = 1;
        Miner.MineSequential(tooEasy);
        Assert.False(chain.TryAddBlock(tooEasy, out error));
        Assert.Contains("the difficulty is 1, but the network expects 2", error);

        var unlinked = chain.CreateCandidateBlock(Bob.Address, [], Genesis.Timestamp + 5000);
        unlinked.PreviousHash = new string('0', 64);
        Miner.MineSequential(unlinked);
        Assert.False(chain.TryAddBlock(unlinked, out error));
        Assert.Contains("does not match the hash of block #1", error);

        Assert.Equal(2, chain.Blocks.Count);
    }

    [Fact]
    public void A_negative_coinbase_amount_is_rejected()
    {
        Assert.False(TransactionValidator.ValidateCoinbase(Transaction.CreateCoinbase(Bob.Address, -5, 3, 0), 3, out var error));
        Assert.Contains("amount must be greater than zero", error);
    }

    [Fact]
    public void A_sender_may_spend_exactly_their_whole_balance()
    {
        var chain = ChainWhereAliceHas50();
        var mempool = new Mempool();

        Assert.True(mempool.TryAdd(Alice.Pay(Bob, 49, nonce: 1, fee: 1), chain, out var error), error);
        chain.MinePendingTransactions(mempool, Carol.Address, Genesis.Timestamp + 5000);

        Assert.Equal(0, chain.GetBalance(Alice.Address));
        Assert.Equal(49, chain.GetBalance(Bob.Address));
    }

    [Fact]
    public void Paying_yourself_only_costs_the_fee()
    {
        var chain = ChainWhereAliceHas50();

        TestChains.MineBlock(chain, Carol, Alice.Pay(Alice, 30, nonce: 1, fee: 2));

        Assert.Equal(48, chain.GetBalance(Alice.Address));
        Assert.Equal(52, chain.GetBalance(Carol.Address));
    }

    [Fact]
    public void Pending_fees_count_against_the_balance_in_the_mempool()
    {
        var chain = ChainWhereAliceHas50();
        var mempool = new Mempool();
        Assert.True(mempool.TryAdd(Alice.Pay(Bob, 40, nonce: 1, fee: 5), chain, out _));

        // Without the pending fee Alice would have 10 left; with it she has 5.
        Assert.False(mempool.TryAdd(Alice.Pay(Bob, 6, nonce: 2), chain, out var error));
        Assert.Contains("only has 5 available", error);
    }

    [Fact]
    public void A_block_may_hold_exactly_the_transaction_limit_and_mining_leaves_the_rest_pending()
    {
        var parameters = new ChainParameters { Difficulty = 1, MaxTransactionsPerBlock = 2 };
        var chain = ChainWhereAliceHas50(parameters);
        var mempool = new Mempool();
        for (var nonce = 1; nonce <= 3; nonce++)
            Assert.True(mempool.TryAdd(Alice.Pay(Bob, 1, nonce), chain, out _));

        var block = chain.MinePendingTransactions(mempool, Carol.Address, Genesis.Timestamp + 5000);

        Assert.Equal(3, block.Transactions.Count); // The coinbase plus exactly the limit.
        Assert.Equal(3, Assert.Single(mempool.Transactions).Nonce);
        Assert.Equal(2, chain.GetNonce(Alice.Address));
    }

    [Fact]
    public void The_limit_is_checked_before_a_block_is_hashed()
    {
        var parameters = new ChainParameters { Difficulty = 1, MaxTransactionsPerBlock = 1 };
        var block = TestChains.NextBlock(TestChains.Create(parameters).Blocks, difficulty: 1);
        block.Transactions = [block.Transactions[0], .. Enumerable.Repeat(Alice.Pay(Bob, 1, nonce: 1), 3)];

        Assert.False(ChainValidator.ValidateProofOfWork(block, parameters, out var error));
        Assert.Contains("the limit is 1", error);
    }

    [Fact]
    public void Both_halves_of_the_genesis_check_matter()
    {
        var withRightHashButWrongContents = Genesis.Create();
        withRightHashButWrongContents.Nonce = 1;
        Assert.Equal(Genesis.Hash, withRightHashButWrongContents.Hash);
        Assert.False(ChainValidator.ValidateChain([withRightHashButWrongContents], ChainParameters.Default, out _));

        var withRightContentsButWrongHash = Genesis.Create();
        withRightContentsButWrongHash.Hash = new string('0', 64);
        Assert.Equal(Genesis.Hash, withRightContentsButWrongHash.ComputeHash());
        Assert.False(ChainValidator.ValidateChain([withRightContentsButWrongHash], ChainParameters.Default, out _));
    }

    [Theory]
    [InlineData(2 * 60 * 60 * 1000, true)]
    [InlineData(2 * 60 * 60 * 1000 + 1, false)]
    public void Blocks_may_be_at_most_two_hours_ahead_of_the_local_clock(long aheadMs, bool expected)
    {
        var now = Genesis.Timestamp + 1_000_000;
        var block = TestChains.NextBlock(TestChains.Create().Blocks, difficulty: 1);
        block.Timestamp = now + aheadMs;

        Assert.Equal(expected, ChainValidator.ValidateNotInFuture(block, now, ChainParameters.Default, out var error));
        if (!expected)
            Assert.Contains("in the future", error);
    }

    [Fact]
    public void Chain_parameters_keep_rewards_far_from_overflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainParameters { BlockReward = ChainParameters.MaxBlockReward + 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainParameters { BlockReward = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainParameters { MaxFutureBlockTimeMs = -1 });
        Assert.Equal(ChainParameters.MaxBlockReward, new ChainParameters { BlockReward = ChainParameters.MaxBlockReward }.BlockReward);
    }

    [Fact]
    public void A_transaction_without_an_id_is_rejected_instead_of_throwing()
    {
        var chain = ChainWhereAliceHas50();
        var mempool = new Mempool();

        Assert.False(mempool.TryAdd(Alice.Pay(Bob, 1, nonce: 1) with { Id = null! }, chain, out var error));
        Assert.Equal("the transaction has no id.", error);
        Assert.False(mempool.TryAdd(Alice.Pay(Bob, 1, nonce: 1) with { Id = "" }, chain, out error));
        Assert.Equal("the transaction has no id.", error);
    }

    [Fact]
    public void A_full_mempool_turns_new_transactions_away()
    {
        var chain = ChainWhereAliceHas50();
        var mempool = new Mempool(capacity: 1);
        Assert.True(mempool.TryAdd(Alice.Pay(Bob, 1, nonce: 1), chain, out _));

        Assert.False(mempool.TryAdd(Alice.Pay(Bob, 1, nonce: 2), chain, out var error));
        Assert.Contains("the mempool is full", error);
    }

    [Fact]
    public void Revalidate_does_not_count_a_returned_transaction_that_is_already_pending_twice()
    {
        var chain = ChainWhereAliceHas50();
        var mempool = new Mempool();
        var pending = Alice.Pay(Bob, 5, nonce: 1);
        var orphaned = Alice.Pay(Bob, 7, nonce: 2);
        Assert.True(mempool.TryAdd(pending, chain, out _));

        var removed = mempool.Revalidate(chain, [pending, orphaned]);

        Assert.Empty(removed);
        Assert.Equal(2, mempool.Count);
        Assert.True(mempool.Contains(pending.Id));
        Assert.True(mempool.Contains(orphaned.Id));
    }
}
