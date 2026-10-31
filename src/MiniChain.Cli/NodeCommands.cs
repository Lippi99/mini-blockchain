using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>
/// Commands that talk to a running node: <c>status</c>, <c>chain</c>, <c>balance</c>, <c>send</c>, <c>mine</c> and
/// <c>peers</c>. The node is chosen with <c>--node</c>, the <c>MINICHAIN_NODE</c> environment variable, or
/// defaults to http://localhost:5001. Transactions are signed here, so private keys never leave this machine.
/// </summary>
internal static class NodeCommands
{
    public static async Task<int> StatusAsync(CommandLine options)
    {
        using var node = Connect(options);
        var status = await node.GetStatusAsync();
        Console.WriteLine($"Node          : {status.Url ?? node.Url}");
        Console.WriteLine($"Height        : {status.Height}");
        Console.WriteLine($"Latest hash   : {status.LatestHash}");
        Console.WriteLine($"Difficulty    : {status.NextDifficulty}");
        Console.WriteLine($"Work          : {status.CumulativeWork}");
        Console.WriteLine($"Pending       : {status.PendingTransactions} transaction(s)");
        Console.WriteLine($"Miner address : {status.MinerAddress}");
        Console.WriteLine($"Peers         : {(status.Peers.Count == 0 ? "none" : string.Join(", ", status.Peers))}");
        return 0;
    }

    public static async Task<int> ChainAsync(CommandLine options)
    {
        using var node = Connect(options);
        var chain = await node.GetChainAsync();
        ConsoleOutput.PrintChain(chain.Blocks, Shorten);
        Console.WriteLine($"{chain.Length} blocks, cumulative work {chain.CumulativeWork}.");
        return 0;
    }

    public static async Task<int> BalanceAsync(CommandLine options)
    {
        var address = AddressFrom(options);
        using var node = Connect(options);
        var account = await node.GetAccountAsync(address);
        Console.WriteLine($"Address    : {account.Address}");
        Console.WriteLine($"Balance    : {account.Balance}");
        Console.WriteLine($"Next nonce : {account.NextNonce}");
        return 0;
    }

    public static async Task<int> SendAsync(CommandLine options)
    {
        var to = options.Require("to");
        if (!Address.IsValid(to))
            throw new CliUsageException($"--to '{to}' is not a valid address (40 lowercase hex characters).");
        var amount = options.GetLong("amount", 0);
        var fee = options.GetLong("fee", 0);

        using var wallet = WalletCommands.Load(options);
        using var node = Connect(options);

        // The node knows which nonce comes next, counting this wallet's transactions that are still pending.
        var account = await node.GetAccountAsync(wallet.Address);
        var transaction = wallet.CreateTransaction(to, amount, fee, account.NextNonce, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await node.SubmitTransactionAsync(transaction);

        Console.WriteLine($"Sent {amount} (fee {fee}) from {wallet.Address} to {to}.");
        Console.WriteLine($"Transaction {transaction.Id} is pending until a node mines it.");
        return 0;
    }

    public static async Task<int> MineAsync(CommandLine options)
    {
        string? minerAddress = options.Has("address") || options.Has("name") ? AddressFrom(options) : null;
        using var node = Connect(options);
        var block = await node.MineAsync(minerAddress);
        Console.WriteLine($"Mined block #{block.Index} {block.Hash} (nonce {block.Nonce}).");
        Console.WriteLine($"It holds {block.Transactions.Count - 1} transaction(s) and pays a reward of {block.Transactions[0].Amount} to {block.Transactions[0].To}.");
        return 0;
    }

    public static async Task<int> PeersAsync(CommandLine options)
    {
        using var node = Connect(options);
        var action = options.FirstArgument ?? "list";
        switch (action)
        {
            case "list":
                var peers = await node.GetPeersAsync();
                Console.WriteLine(peers.Count == 0 ? "No peers." : string.Join(Environment.NewLine, peers));
                return 0;
            case "add":
                await node.AddPeerAsync(PeerUrl(options));
                Console.WriteLine($"{node.Url} now knows {PeerUrl(options)}, and the two nodes will sync.");
                return 0;
            case "remove":
                await node.RemovePeerAsync(PeerUrl(options));
                Console.WriteLine($"{node.Url} no longer talks to {PeerUrl(options)}.");
                return 0;
            default:
                throw new CliUsageException("Usage: peers [list | add <url> | remove <url>] [--node <url>]");
        }
    }

    private static NodeClient Connect(CommandLine options) =>
        new(options.Get("node") ?? Environment.GetEnvironmentVariable("MINICHAIN_NODE") ?? "http://localhost:5001");

    private static string AddressFrom(CommandLine options)
    {
        if (options.Get("address") is { } address)
        {
            return Address.IsValid(address)
                ? address
                : throw new CliUsageException($"--address '{address}' is not a valid address (40 lowercase hex characters).");
        }

        using var wallet = WalletCommands.Load(options);
        return wallet.Address;
    }

    private static string PeerUrl(CommandLine options) =>
        options.Arguments.Skip(1).FirstOrDefault() ?? throw new CliUsageException("Give the peer's URL, for example: peers add http://localhost:5002");

    private static string Shorten(string address) => address.Length > 12 ? $"{address[..8]}..." : address;
}
