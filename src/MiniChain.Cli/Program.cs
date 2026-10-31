using MiniChain.Cli;

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    PrintHelp();
    return 0;
}

var options = CommandLine.Parse(args[1..]);
try
{
    return args[0] switch
    {
        "demo" => ChainDemo.Run(),
        "tamper" => TamperDemo.Run(),
        "bench" => Benchmark.Run(options),
        "ledger" => LedgerDemo.Run(),
        "signatures" => SignatureDemo.Run(),
        "wallet" => WalletCommands.Run(options),
        "status" => await NodeCommands.StatusAsync(options),
        "chain" => await NodeCommands.ChainAsync(options),
        "balance" => await NodeCommands.BalanceAsync(options),
        "send" => await NodeCommands.SendAsync(options),
        "mine" => await NodeCommands.MineAsync(options),
        "peers" => await NodeCommands.PeersAsync(options),
        _ => Unknown(args[0]),
    };
}
catch (CliUsageException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintHelp();
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

        Talking to a node (--node <url>, or MINICHAIN_NODE, default http://localhost:5001):
          status                                    Show the node's height, tip, difficulty and peers
          chain                                     Print the node's whole chain
          balance  [--name <wallet> | --address <a>] Show an address's balance and next nonce
          send     --to <address> --amount <n> [--fee <n>] [--name <wallet>]
                                                    Sign a payment with a wallet and submit it
          mine     [--name <wallet> | --address <a>] Ask the node to mine a block paying you the reward
          peers    [list | add <url> | remove <url>] Manage the node's peers
        """);
}
