using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>The Phase 4 experiment, extended with proof of work: rewrite history and watch validation catch it.</summary>
internal static class TamperDemo
{
    public static int Run()
    {
        var chain = new Blockchain();
        var mempool = new Mempool();
        var accounts = new DemoAccounts();
        var (alice, bob, carol, mallory) = (accounts["Alice"], accounts["Bob"], accounts["Carol"], accounts["Mallory"]);

        chain.MinePendingTransactions(mempool, alice);
        chain.MinePendingTransactions(mempool, bob);
        mempool.TryAdd(Transaction.Create(bob, carol, amount: 10, fee: 1, nonce: 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), chain, out _);
        chain.MinePendingTransactions(mempool, carol);

        Report(1, $"Mine a valid chain of 4 blocks at difficulty {chain.Parameters.Difficulty}. Alice mined block #1.", chain);

        var block1 = chain.Blocks[1];
        var reward = block1.Transactions[0];
        block1.Transactions = [Transaction.CreateCoinbase(mallory, reward.Amount, reward.Nonce, reward.Timestamp)];
        Report(2, "Mallory rewrites block #1 so that its reward pays her instead of Alice.", chain);

        block1.Hash = block1.ComputeHash();
        Report(3, "She recomputes block #1's hash to hide the change.", chain);

        Miner.MineParallel(block1);
        Report(4, "She re-mines block #1 so its hash has the required zeros again.", chain);

        for (var i = 2; i < chain.Blocks.Count; i++)
        {
            chain.Blocks[i].PreviousHash = chain.Blocks[i - 1].Hash;
            Miner.MineParallel(chain.Blocks[i]);
        }
        Report(5, "She re-links and re-mines every block after it.", chain);

        Console.WriteLine("Rewriting one block means redoing the proof of work for that block and every block after");
        Console.WriteLine("it. On a real network the honest nodes keep extending their chain in the meantime, so a");
        Console.WriteLine("forger has to out-mine all of them to catch up.");
        return 0;
    }

    private static void Report(int step, string description, Blockchain chain)
    {
        Console.WriteLine($"Step {step}: {description}");
        Console.WriteLine(chain.IsValid(out var error) ? "  IsValid = true" : $"  IsValid = false -> {error}");
        Console.WriteLine();
    }
}
