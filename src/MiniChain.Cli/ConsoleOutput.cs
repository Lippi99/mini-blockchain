using System.Globalization;
using MiniChain.Core;

namespace MiniChain.Cli;

internal static class ConsoleOutput
{
    public static void PrintChain(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
            PrintBlock(block);
    }

    public static void PrintBlock(Block block)
    {
        Console.WriteLine($"Block #{block.Index}");
        Console.WriteLine($"  Timestamp     : {FormatTime(block.Timestamp)}");
        Console.WriteLine($"  Previous hash : {block.PreviousHash}");
        Console.WriteLine($"  Hash          : {block.Hash}");
        Console.WriteLine($"  Difficulty    : {block.Difficulty}");
        Console.WriteLine($"  Nonce         : {block.Nonce}");
        Console.WriteLine($"  Transactions  : {block.Transactions.Count}");
        foreach (var transaction in block.Transactions)
            Console.WriteLine($"    - {transaction}");
        Console.WriteLine();
    }

    public static string FormatTime(long unixMilliseconds)
    {
        const long maxUnixMilliseconds = 253_402_300_799_999; // 9999-12-31T23:59:59.999Z
        if (unixMilliseconds is < 0 or > maxUnixMilliseconds)
            return unixMilliseconds.ToString(CultureInfo.InvariantCulture);

        return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
            .ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
