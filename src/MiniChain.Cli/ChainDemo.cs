using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>Mines a small chain with a few payments and prints it with the resulting balances.</summary>
internal static class ChainDemo
{
    public static int Run()
    {
        var chain = new Blockchain();
        var mempool = new Mempool();
        var accounts = new DemoAccounts();
        var (alice, bob, carol) = (accounts["Alice"], accounts["Bob"], accounts["Carol"]);

        // Alice mines the first block and earns the reward: the only way new coins appear.
        chain.MinePendingTransactions(mempool, alice);

        Submit(mempool, chain, Transaction.Create(alice, bob, amount: 20, fee: 2, nonce: 1, Now()));
        Submit(mempool, chain, Transaction.Create(alice, carol, amount: 5, fee: 1, nonce: 2, Now()));
        chain.MinePendingTransactions(mempool, bob); // Bob earns the reward plus Alice's fees.

        Submit(mempool, chain, Transaction.Create(bob, carol, amount: 10, fee: 1, nonce: 1, Now()));
        chain.MinePendingTransactions(mempool, carol);

        ConsoleOutput.PrintChain(chain.Blocks, accounts.NameOf);
        ConsoleOutput.PrintBalances(chain, accounts.NameOf);
        Console.WriteLine($"The total equals {chain.Blocks.Count - 1} block rewards of {chain.Parameters.BlockReward}: fees only move coins around.");
        Console.WriteLine($"Chain valid: {chain.IsValid(out var error)}{(error is null ? "" : $" ({error})")}");
        return 0;
    }

    private static void Submit(Mempool mempool, Blockchain chain, Transaction transaction)
    {
        if (!mempool.TryAdd(transaction, chain, out var error))
            throw new InvalidOperationException($"The demo transaction was rejected: {error}");
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
