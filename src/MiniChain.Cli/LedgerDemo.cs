using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>The Phase 6 experiments: try to break the transaction rules and watch each attempt get rejected.</summary>
internal static class LedgerDemo
{
    public static int Run()
    {
        var chain = new Blockchain();
        var mempool = new Mempool();
        var accounts = new DemoAccounts();
        var (alice, bob, carol) = (accounts["Alice"], accounts["Bob"], accounts["Carol"]);

        chain.MinePendingTransactions(mempool, alice);
        Console.WriteLine($"Alice mines block #1 and now has {chain.GetBalance(alice)} coins.");
        Console.WriteLine();

        Try("Alice tries to send Bob 80 coins.",
            mempool, chain, Transaction.Create(alice, bob, amount: 80, fee: 0, nonce: 1, Now()));

        var payment = Transaction.Create(alice, bob, amount: 10, fee: 1, nonce: 1, Now());
        Try("Alice sends Bob 10 coins with a fee of 1.", mempool, chain, payment);
        Try("The same transaction is submitted a second time.", mempool, chain, payment);

        Try("Alice tries to spend 45 more while the first payment is pending (a double spend).",
            mempool, chain, Transaction.Create(alice, carol, amount: 45, fee: 0, nonce: 2, Now()));
        Try("Alice skips ahead to nonce 5.",
            mempool, chain, Transaction.Create(alice, carol, amount: 1, fee: 0, nonce: 5, Now()));
        Try("Alice tries to send 0 coins.",
            mempool, chain, Transaction.Create(alice, carol, amount: 0, fee: 0, nonce: 2, Now()));

        var block = chain.MinePendingTransactions(mempool, carol);
        Console.WriteLine($"Carol mines block #{block.Index}; the payment is confirmed. Balances: Alice {chain.GetBalance(alice)}, Bob {chain.GetBalance(bob)}, Carol {chain.GetBalance(carol)}.");
        Console.WriteLine();

        Try("Bob replays Alice's confirmed payment to get paid twice.", mempool, chain, payment);

        Console.WriteLine("A miner tries to break the block rules:");
        var tooGreedy = chain.CreateCandidateBlock(bob, [], Now());
        tooGreedy.Transactions = [Transaction.CreateCoinbase(bob, chain.Parameters.BlockReward + 1, tooGreedy.Index, tooGreedy.Timestamp)];
        TryBlock("  A coinbase that pays more than the block reward plus fees.", chain, tooGreedy);

        var twoRewards = chain.CreateCandidateBlock(bob, [], Now());
        twoRewards.Transactions = [.. twoRewards.Transactions, Transaction.CreateCoinbase(carol, 1, twoRewards.Index, twoRewards.Timestamp)];
        TryBlock("  A block with a second coinbase.", chain, twoRewards);
        return 0;
    }

    private static void Try(string description, Mempool mempool, Blockchain chain, Transaction transaction)
    {
        Console.WriteLine(description);
        Console.WriteLine(mempool.TryAdd(transaction, chain, out var error) ? "  -> accepted into the mempool" : $"  -> rejected: {error}");
        Console.WriteLine();
    }

    private static void TryBlock(string description, Blockchain chain, Block block)
    {
        Miner.MineParallel(block);
        Console.WriteLine(description);
        Console.WriteLine(chain.TryAddBlock(block, out var error) ? "    -> accepted" : $"    -> rejected: {error}");
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
