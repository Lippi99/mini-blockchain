using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>The Phase 4 experiment, extended with proof of work: change history and watch validation catch it.</summary>
internal static class TamperDemo
{
    public static int Run()
    {
        var chain = new Blockchain();
        foreach (var payment in new[] { "Alice pays Bob 5", "Bob pays Carol 2", "Carol pays Dave 1" })
            chain.AddBlock(new Block { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Transactions = [payment] });

        Report(1, $"Build a valid chain of 4 blocks, mined at difficulty {chain.Parameters.Difficulty}.", chain);

        var block1 = chain.Blocks[1];
        block1.Transactions = ["Alice pays Bob 500"];
        Report(2, "Change the transaction inside block #1 to \"Alice pays Bob 500\".", chain);

        block1.Hash = block1.ComputeHash();
        Report(3, "Recompute block #1's hash to hide the change.", chain);

        Miner.MineParallel(block1);
        Report(4, "Re-mine block #1 so its hash has the required zeros again.", chain);

        for (var i = 2; i < chain.Blocks.Count; i++)
        {
            chain.Blocks[i].PreviousHash = chain.Blocks[i - 1].Hash;
            Miner.MineParallel(chain.Blocks[i]);
        }
        Report(5, "Re-link and re-mine every block after it.", chain);

        Console.WriteLine("Rewriting one block means redoing the proof of work for that block and every block after");
        Console.WriteLine("it. On a real network the honest nodes keep extending their chain in the meantime, so the");
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
