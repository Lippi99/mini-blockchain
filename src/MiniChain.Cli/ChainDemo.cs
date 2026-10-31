using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>Builds a small chain and prints it, showing how each block points to the one before it.</summary>
internal static class ChainDemo
{
    public static int Run()
    {
        var chain = new Blockchain();
        string[][] payloads =
        [
            ["Alice pays Bob 5"],
            ["Bob pays Carol 2", "Carol pays Dave 1"],
            ["Dave pays Alice 1"],
        ];

        foreach (var transactions in payloads)
        {
            chain.AddBlock(new Block
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Transactions = transactions,
            });
        }

        ConsoleOutput.PrintChain(chain.Blocks);
        Console.WriteLine($"Every block's previous hash equals the hash of the block before it: {IsLinked(chain)}");
        return 0;
    }

    private static bool IsLinked(Blockchain chain)
    {
        for (var i = 1; i < chain.Blocks.Count; i++)
        {
            if (chain.Blocks[i].PreviousHash != chain.Blocks[i - 1].Hash)
                return false;
        }
        return true;
    }
}
