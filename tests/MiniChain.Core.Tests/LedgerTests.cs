namespace MiniChain.Core.Tests;

/// <summary>Balances, rewards and the transaction rules a block must follow.</summary>
public class LedgerTests
{
    private static readonly TestAccount Alice = TestAccounts.Alice;
    private static readonly TestAccount Bob = TestAccounts.Bob;
    private static readonly TestAccount Carol = TestAccounts.Carol;

    /// <summary>A chain where Alice mined block #1 and owns the 50-coin reward.</summary>
    private static Blockchain ChainWhereAliceHas50()
    {
        var chain = TestChains.Create();
        TestChains.MineBlock(chain, Alice);
        return chain;
    }

    private static string Reject(Blockchain chain, Block block)
    {
        var heightBefore = chain.Blocks.Count;
        Assert.False(TestChains.TryMineAndAdd(chain, block, out var error));
        Assert.Equal(heightBefore, chain.Blocks.Count);
        return error!;
    }

    [Fact]
    public void Balances_are_correct_after_several_blocks()
    {
        var chain = ChainWhereAliceHas50();
        TestChains.MineBlock(chain, Bob, Alice.Pay(Bob, 20, nonce: 1, fee: 2), Alice.Pay(Carol, 5, nonce: 2, fee: 1));
        TestChains.MineBlock(chain, Carol, Bob.Pay(Carol, 10, nonce: 1, fee: 1));

        Assert.Equal(50 - 20 - 2 - 5 - 1, chain.GetBalance(Alice.Address));
        Assert.Equal(50 + 3 + 20 - 10 - 1, chain.GetBalance(Bob.Address));
        Assert.Equal(5 + 10 + 50 + 1, chain.GetBalance(Carol.Address));
        Assert.Equal(3 * chain.Parameters.BlockReward, chain.Balances.Values.Sum());
        Assert.Equal(2, chain.GetNonce(Alice.Address));
        Assert.Equal(1, chain.GetNonce(Bob.Address));
        Assert.Equal(0, chain.GetNonce(Carol.Address));
        Assert.True(chain.IsValid(out var error), error);
    }

    [Fact]
    public void A_block_without_a_coinbase_is_rejected()
    {
        var chain = ChainWhereAliceHas50();
        var block = chain.CreateCandidateBlock(Bob.Address, [], Genesis.Timestamp);
        block.Transactions = [];

        Assert.Contains("no coinbase", Reject(chain, block));
    }

    [Fact]
    public void The_coinbase_must_come_first()
    {
        var chain = ChainWhereAliceHas50();
        var block = chain.CreateCandidateBlock(Bob.Address, [Alice.Pay(Bob, 5, nonce: 1)], Genesis.Timestamp);
        block.Transactions = [block.Transactions[1], block.Transactions[0]];

        Assert.Contains("first transaction must be the coinbase", Reject(chain, block));
    }

    [Fact]
    public void A_second_coinbase_is_rejected()
    {
        var chain = ChainWhereAliceHas50();
        var block = chain.CreateCandidateBlock(Bob.Address, [], Genesis.Timestamp);
        block.Transactions = [.. block.Transactions, Transaction.CreateCoinbase(Carol.Address, 1, block.Index, block.Timestamp)];

        Assert.Contains("only the first transaction of a block can be a coinbase", Reject(chain, block));
    }

    [Fact]
    public void The_coinbase_may_claim_the_reward_plus_fees_but_not_more()
    {
        var chain = ChainWhereAliceHas50();
        var fees = 3;
        var greedy = chain.CreateCandidateBlock(Bob.Address, [Alice.Pay(Carol, 5, nonce: 1, fee: fees)], Genesis.Timestamp);
        TestChains.ChangeReward(greedy, 50 + fees + 1);

        Assert.Contains("the block reward plus fees is only 53", Reject(chain, greedy));

        var exact = chain.CreateCandidateBlock(Bob.Address, [Alice.Pay(Carol, 5, nonce: 1, fee: fees)], Genesis.Timestamp);
        Assert.Equal(50 + fees, exact.Transactions[0].Amount);
        Assert.True(TestChains.TryMineAndAdd(chain, exact, out var error), error);
    }

    [Fact]
    public void A_miner_may_claim_less_than_the_full_reward()
    {
        var chain = ChainWhereAliceHas50();
        var modest = chain.CreateCandidateBlock(Bob.Address, [], Genesis.Timestamp);
        TestChains.ChangeReward(modest, 10);

        Assert.True(TestChains.TryMineAndAdd(chain, modest, out var error), error);
        Assert.Equal(10, chain.GetBalance(Bob.Address));
    }

    [Fact]
    public void A_coinbase_with_the_wrong_nonce_is_rejected()
    {
        var chain = ChainWhereAliceHas50();
        var block = chain.CreateCandidateBlock(Bob.Address, [], Genesis.Timestamp);
        block.Transactions = [Transaction.CreateCoinbase(Bob.Address, 50, blockIndex: 1, block.Timestamp)];

        Assert.Contains("must equal the block index 2", Reject(chain, block));
    }

    [Fact]
    public void Overspending_is_rejected()
    {
        var chain = ChainWhereAliceHas50();

        var error = Reject(chain, chain.CreateCandidateBlock(Bob.Address, [Alice.Pay(Bob, 50, nonce: 1, fee: 1)], Genesis.Timestamp));

        Assert.Contains("spends 51, but the sender only has 50", error);
    }

    [Fact]
    public void Earlier_transactions_in_the_same_block_count_against_the_balance()
    {
        var chain = ChainWhereAliceHas50();
        var doubleSpend = chain.CreateCandidateBlock(Bob.Address,
            [Alice.Pay(Bob, 30, nonce: 1), Alice.Pay(Carol, 30, nonce: 2)], Genesis.Timestamp);

        Assert.Contains("spends 30, but the sender only has 20", Reject(chain, doubleSpend));
    }

    [Fact]
    public void Coins_received_earlier_in_the_same_block_can_be_spent()
    {
        var chain = ChainWhereAliceHas50();

        TestChains.MineBlock(chain, Carol, Alice.Pay(Bob, 30, nonce: 1), Bob.Pay(Carol, 25, nonce: 1));

        Assert.Equal(5, chain.GetBalance(Bob.Address));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void The_nonce_must_be_the_senders_next_one(long nonce)
    {
        var chain = ChainWhereAliceHas50();

        var error = Reject(chain, chain.CreateCandidateBlock(Bob.Address, [Alice.Pay(Bob, 5, nonce)], Genesis.Timestamp));

        Assert.Contains($"has nonce {nonce}, but the sender's next nonce is 1", error);
    }

    [Fact]
    public void A_used_nonce_cannot_be_used_again()
    {
        var chain = ChainWhereAliceHas50();
        TestChains.MineBlock(chain, Carol, Alice.Pay(Bob, 5, nonce: 1));

        var error = Reject(chain, chain.CreateCandidateBlock(Carol.Address, [Alice.Pay(Bob, 6, nonce: 1)], Genesis.Timestamp));

        Assert.Contains("has nonce 1, but the sender's next nonce is 2", error);
    }

    [Fact]
    public void The_same_transaction_twice_in_one_block_is_rejected()
    {
        var chain = ChainWhereAliceHas50();
        var payment = Alice.Pay(Bob, 5, nonce: 1);

        Assert.Contains("is a duplicate", Reject(chain, chain.CreateCandidateBlock(Carol.Address, [payment, payment], Genesis.Timestamp)));
    }

    [Fact]
    public void Replaying_a_confirmed_transaction_is_rejected()
    {
        var chain = ChainWhereAliceHas50();
        var payment = Alice.Pay(Bob, 5, nonce: 1);
        TestChains.MineBlock(chain, Carol, payment);

        Assert.Contains("is a duplicate", Reject(chain, chain.CreateCandidateBlock(Carol.Address, [payment], Genesis.Timestamp)));
    }

    [Fact]
    public void A_mining_reward_can_only_be_spent_from_the_next_block_on()
    {
        var chain = TestChains.Create();

        var error = Reject(chain, chain.CreateCandidateBlock(Alice.Address, [Alice.Pay(Bob, 10, nonce: 1)], Genesis.Timestamp));

        Assert.Contains("the sender only has 0", error);
    }

    [Fact]
    public void A_rejected_block_changes_no_balances()
    {
        var chain = ChainWhereAliceHas50();
        var block = chain.CreateCandidateBlock(Carol.Address,
            [Alice.Pay(Bob, 10, nonce: 1), Alice.Pay(Bob, 1000, nonce: 2)], Genesis.Timestamp);

        Reject(chain, block);

        Assert.Equal(50, chain.GetBalance(Alice.Address));
        Assert.Equal(0, chain.GetBalance(Bob.Address));
        Assert.Equal(0, chain.GetBalance(Carol.Address));
        Assert.Equal(0, chain.GetNonce(Alice.Address));
        Assert.False(chain.ContainsTransaction(block.Transactions[1].Id));
    }

    [Fact]
    public void The_transaction_limit_per_block_is_enforced()
    {
        var chain = TestChains.Create(new ChainParameters { Difficulty = 1, MaxTransactionsPerBlock = 1 });
        TestChains.MineBlock(chain, Alice);

        var error = Reject(chain, chain.CreateCandidateBlock(Bob.Address,
            [Alice.Pay(Bob, 1, nonce: 1), Alice.Pay(Bob, 1, nonce: 2)], Genesis.Timestamp));

        Assert.Contains("the limit is 1", error);
    }

    [Fact]
    public void Full_validation_replays_the_transaction_rules()
    {
        var chain = ChainWhereAliceHas50();
        var overspend = chain.CreateCandidateBlock(Bob.Address, [Alice.Pay(Bob, 100, nonce: 1)], Genesis.Timestamp);
        Miner.MineSequential(overspend);

        Assert.False(ChainValidator.ValidateChain([.. chain.Blocks, overspend], chain.Parameters, out var error));
        Assert.StartsWith("Block #2: transaction #1", error);
        Assert.Contains("spends 100, but the sender only has 50", error);
    }

    [Fact]
    public void A_transaction_edited_inside_a_remined_block_is_still_caught()
    {
        var chain = ChainWhereAliceHas50();
        TestChains.MineBlock(chain, Carol, Alice.Pay(Bob, 5, nonce: 1));
        var block = chain.Blocks[2];

        block.Transactions = [block.Transactions[0], block.Transactions[1] with { Amount = 45 }];
        Miner.MineSequential(block);

        Assert.False(chain.IsValid(out var error));
        Assert.StartsWith("Block #2: transaction #1", error);
        Assert.Contains("id does not match", error);
    }
}
