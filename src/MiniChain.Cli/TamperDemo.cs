using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>The Phase 4 experiment: change history and watch validation catch it.</summary>
internal static class TamperDemo
{
    public static int Run()
    {
        var chain = new Blockchain();
        foreach (var payment in new[] { "Alice pays Bob 5", "Bob pays Carol 2", "Carol pays Dave 1" })
            chain.AddBlock(new Block { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Transactions = [payment] });

        Report(1, "Build a valid chain of 4 blocks.", chain);

        chain.Blocks[1].Transactions = ["Alice pays Bob 500"];
        Report(2, "Change the transaction inside block #1 to \"Alice pays Bob 500\".", chain);

        chain.Blocks[1].Hash = chain.Blocks[1].ComputeHash();
        Report(3, "Recompute block #1's hash to hide the change.", chain);

        Console.WriteLine("Fixing one block always breaks the link from the block after it, so a forger has to");
        Console.WriteLine("rewrite every later block too.");
        return 0;
    }

    private static void Report(int step, string description, Blockchain chain)
    {
        Console.WriteLine($"Step {step}: {description}");
        Console.WriteLine(chain.IsValid(out var error) ? "  IsValid = true" : $"  IsValid = false -> {error}");
        Console.WriteLine();
    }
}
