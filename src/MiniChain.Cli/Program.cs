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
        case "signatures":
            return SignatureDemo.Run();
        case "wallet":
            return WalletCommands.Run(options);
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

        Experiments (each runs on its own in-memory chain):
          demo         Mine a small chain with a few signed payments and print it
          tamper       Rewrite a block and watch validation detect it
          bench        Benchmark the miners [--max-difficulty 6] [--samples 3]
          ledger       Try to overspend, double spend and replay, and watch each attempt fail
          signatures   Forge and tamper with signed transactions, and watch verification fail

        Wallets (saved as wallets/<name>.pem, which Git ignores):
          wallet new   [--name default] [--password <pw>]   Create a key pair and print its address
          wallet show  [--name default] [--password <pw>]   Print a wallet's address and public key
                       The password can also come from MINICHAIN_WALLET_PASSWORD.
        """);
}
