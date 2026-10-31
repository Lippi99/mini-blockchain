using System.Globalization;
using MiniChain.Core;

namespace MiniChain.Cli;

internal static class ConsoleOutput
{
    public static void PrintChain(IEnumerable<Block> blocks, Func<string, string>? nameOf = null)
    {
        foreach (var block in blocks)
            PrintBlock(block, nameOf);
    }

    public static void PrintBlock(Block block, Func<string, string>? nameOf = null)
    {
        Console.WriteLine($"Block #{block.Index}");
        Console.WriteLine($"  Timestamp     : {FormatTime(block.Timestamp)}");
        Console.WriteLine($"  Previous hash : {block.PreviousHash}");
        Console.WriteLine($"  Hash          : {block.Hash}");
        Console.WriteLine($"  Difficulty    : {block.Difficulty}");
        Console.WriteLine($"  Nonce         : {block.Nonce}");
        Console.WriteLine($"  Transactions  : {block.Transactions.Count}");
        foreach (var transaction in block.Transactions)
            Console.WriteLine($"    - {Describe(transaction, nameOf)}");
        Console.WriteLine();
    }

    public static string Describe(Transaction transaction, Func<string, string>? nameOf = null)
    {
        nameOf ??= address => address;
        return transaction.IsCoinbase
            ? $"reward of {transaction.Amount} to {nameOf(transaction.To)}"
            : $"{nameOf(transaction.From)} pays {nameOf(transaction.To)} {transaction.Amount} (fee {transaction.Fee}, nonce {transaction.Nonce})";
    }

    public static void PrintBalances(Blockchain chain, Func<string, string>? nameOf = null)
    {
        nameOf ??= address => address;
        Console.WriteLine("Balances:");
        foreach (var (address, balance) in chain.Balances.OrderBy(pair => nameOf(pair.Key), StringComparer.Ordinal))
            Console.WriteLine($"  {nameOf(address),-12} {balance,6}");
        Console.WriteLine($"  {"total",-12} {chain.Balances.Values.Sum(),6}");
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
