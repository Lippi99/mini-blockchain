namespace MiniChain.Core.Tests;

public class MempoolTests
{
    private static readonly TestAccount Alice = TestAccounts.Alice;
    private static readonly TestAccount Bob = TestAccounts.Bob;
    private static readonly TestAccount Carol = TestAccounts.Carol;

    private readonly Blockchain _chain = TestChains.Create();
    private readonly Mempool _mempool = new();

    public MempoolTests()
    {
        TestChains.MineBlock(_chain, Alice); // Alice owns 50 coins.
    }

    private void Add(Transaction transaction)
    {
        Assert.True(_mempool.TryAdd(transaction, _chain, out var error), error);
    }

    private string Reject(Transaction transaction)
    {
        var countBefore = _mempool.Count;
        Assert.False(_mempool.TryAdd(transaction, _chain, out var error));
        Assert.Equal(countBefore, _mempool.Count);
        return error!;
    }

    [Fact]
    public void A_valid_transaction_is_accepted()
    {
        var payment = Alice.Pay(Bob, 10, nonce: 1, fee: 1);

        Add(payment);

        Assert.True(_mempool.Contains(payment.Id));
        Assert.Equal([payment], _mempool.Transactions);
    }

    [Fact]
    public void The_same_transaction_is_rejected_the_second_time()
    {
        var payment = Alice.Pay(Bob, 10, nonce: 1);
        Add(payment);

        Assert.Equal("the transaction is already pending.", Reject(payment));
    }

    [Fact]
    public void A_confirmed_transaction_cannot_be_replayed()
    {
        var payment = Alice.Pay(Bob, 10, nonce: 1);
        TestChains.MineBlock(_chain, Carol, payment);

        Assert.Equal("the transaction is already in the chain.", Reject(payment));
    }

    [Fact]
    public void Spending_more_than_the_balance_is_rejected()
    {
        Assert.Contains("spends 51, but the sender only has 50 available", Reject(Alice.Pay(Bob, 50, nonce: 1, fee: 1)));
    }

    [Fact]
    public void Pending_spends_count_against_the_balance()
    {
        Add(Alice.Pay(Bob, 30, nonce: 1));

        Assert.Contains("spends 30, but the sender only has 20 available", Reject(Alice.Pay(Carol, 30, nonce: 2)));
    }

    [Fact]
    public void The_nonce_must_follow_the_senders_pending_transactions()
    {
        Add(Alice.Pay(Bob, 1, nonce: 1));

        Assert.Contains("nonce is 1, but the sender's next nonce is 2", Reject(Alice.Pay(Bob, 2, nonce: 1)));
        Assert.Contains("nonce is 3, but the sender's next nonce is 2", Reject(Alice.Pay(Bob, 3, nonce: 3)));
        Add(Alice.Pay(Bob, 2, nonce: 2));
    }

    [Fact]
    public void Transactions_that_break_a_rule_are_rejected()
    {
        Assert.Contains("amount must be greater than zero", Reject(Alice.Pay(Bob, 0, nonce: 1)));
        Assert.Contains("only the first transaction", Reject(Transaction.CreateCoinbase(Bob.Address, 50, 2, 0)));
    }

    [Fact]
    public void Selection_prefers_high_fees_but_keeps_each_senders_nonce_order()
    {
        TestChains.MineBlock(_chain, Bob); // Bob owns 50 coins too.
        var aliceFirst = Alice.Pay(Carol, 1, nonce: 1, fee: 1);
        var aliceSecond = Alice.Pay(Carol, 1, nonce: 2, fee: 10);
        var bobFirst = Bob.Pay(Carol, 1, nonce: 1, fee: 5);
        Add(aliceFirst);
        Add(aliceSecond);
        Add(bobFirst);

        // Alice's high-fee second transaction has to wait for her low-fee first one.
        Assert.Equal([bobFirst, aliceFirst, aliceSecond], _mempool.SelectForBlock(10));
        Assert.Equal([bobFirst, aliceFirst], _mempool.SelectForBlock(2));
    }

    [Fact]
    public void Mining_pays_the_fees_to_the_miner_and_empties_the_mempool()
    {
        Add(Alice.Pay(Bob, 10, nonce: 1, fee: 2));
        Add(Alice.Pay(Bob, 5, nonce: 2, fee: 1));

        var block = _chain.MinePendingTransactions(_mempool, Carol.Address, timestamp: Genesis.Timestamp + 5000);

        Assert.Equal(3, block.Transactions.Count);
        Assert.Equal(_chain.Parameters.BlockReward + 3, block.Transactions[0].Amount);
        Assert.Equal(0, _mempool.Count);
        Assert.Equal(50 - 18, _chain.GetBalance(Alice.Address));
        Assert.Equal(15, _chain.GetBalance(Bob.Address));
        Assert.Equal(53, _chain.GetBalance(Carol.Address));
    }

    [Fact]
    public void Revalidate_drops_transactions_that_conflict_with_a_new_block()
    {
        var pendingFirst = Alice.Pay(Bob, 10, nonce: 1);
        var pendingSecond = Alice.Pay(Bob, 5, nonce: 2);
        Add(pendingFirst);
        Add(pendingSecond);

        // Another miner confirms a different nonce-1 transaction from Alice.
        TestChains.MineBlock(_chain, Carol, Alice.Pay(Carol, 40, nonce: 1));
        var removed = _mempool.Revalidate(_chain);

        Assert.Equal([pendingFirst], removed);
        Assert.Equal([pendingSecond], _mempool.Transactions);
    }

    [Fact]
    public void Revalidate_takes_back_still_valid_transactions_from_discarded_blocks_in_nonce_order()
    {
        // Returned out of order: the second payment would be rejected if it were re-added first.
        var orphanedFirst = Alice.Pay(Bob, 10, nonce: 1);
        var orphanedSecond = Alice.Pay(Bob, 5, nonce: 2);

        var removed = _mempool.Revalidate(_chain, [orphanedSecond, orphanedFirst]);

        Assert.Empty(removed);
        Assert.Equal(2, _mempool.Count);
        Assert.True(_mempool.Contains(orphanedFirst.Id));
        Assert.True(_mempool.Contains(orphanedSecond.Id));
    }
}
