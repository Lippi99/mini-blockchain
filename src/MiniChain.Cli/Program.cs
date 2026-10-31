using MiniChain.Cli;

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    PrintHelp();
    return 0;
}

var options = CommandLine.Parse(args[1..]);
try
{
    switch (args[0])
    {
        case "demo":
            return ChainDemo.Run();
        case "tamper":
            return TamperDemo.Run();
        case "bench":
            return Benchmark.Run(options);
        case "ledger":
            return LedgerDemo.Run();
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintHelp();
            return 1;
    }
}
catch (CliUsageException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
        MiniChain CLI

        Usage: dotnet run --project src/MiniChain.Cli -- <command> [options]

        Commands:
          demo      Mine a small chain with a few payments and print it
          tamper    Rewrite a block and watch validation detect it
          bench     Benchmark the miners [--max-difficulty 6] [--samples 3]
          ledger    Try to overspend, double spend and replay, and watch each attempt fail
        """);
}
