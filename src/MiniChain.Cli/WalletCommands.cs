using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>
/// <c>wallet new</c> and <c>wallet show</c>. Wallets are PEM files in the <c>wallets/</c> folder, which
/// is excluded from Git. A password can be passed with <c>--password</c> or, to keep it out of the
/// shell history, the <c>MINICHAIN_WALLET_PASSWORD</c> environment variable.
/// </summary>
internal static partial class WalletCommands
{
    public static int Run(CommandLine options)
    {
        var action = options.FirstArgument
            ?? throw new CliUsageException("Usage: wallet new|show [--name <name>] [--password <password>]");

        switch (action)
        {
            case "new":
                return New(options);
            case "show":
                return Show(options);
            default:
                throw new CliUsageException($"Unknown wallet command '{action}'. Use 'new' or 'show'.");
        }
    }

    private static int New(CommandLine options)
    {
        var path = PathFor(options);
        if (File.Exists(path))
            throw new CliUsageException($"{path} already exists. Pick another --name; overwriting would destroy that key.");

        using var wallet = new Wallet();
        var password = Password(options);
        wallet.Save(path, password);

        Console.WriteLine($"Created wallet '{NameOf(options)}'.");
        Console.WriteLine($"  Address : {wallet.Address}");
        Console.WriteLine($"  Key file: {Path.GetFullPath(path)}{(password is null ? " (not encrypted)" : " (encrypted)")}");
        Console.WriteLine("Keep the key file secret: whoever holds it can spend this address's coins.");
        return 0;
    }

    private static int Show(CommandLine options)
    {
        using var wallet = Load(options);
        Console.WriteLine($"Wallet '{NameOf(options)}'");
        Console.WriteLine($"  Address   : {wallet.Address}");
        Console.WriteLine($"  Public key: {wallet.PublicKeyHex}");
        return 0;
    }

    /// <summary>Loads the wallet chosen with <c>--name</c>, turning file and password problems into usage errors.</summary>
    public static Wallet Load(CommandLine options)
    {
        var path = PathFor(options);
        if (!File.Exists(path))
            throw new CliUsageException($"No wallet at {path}. Create one with: wallet new --name {NameOf(options)}");

        try
        {
            return Wallet.Load(path, Password(options));
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException or PlatformNotSupportedException)
        {
            throw new CliUsageException($"Could not open {path}: wrong or missing password, or not a wallet file? ({e.Message})");
        }
    }

    private static string NameOf(CommandLine options)
    {
        var name = options.Get("name") ?? "default";
        return WalletName().IsMatch(name)
            ? name
            : throw new CliUsageException("--name may only contain letters, digits, '-' and '_'.");
    }

    private static string PathFor(CommandLine options) =>
        Path.Combine(options.Get("wallet-dir") ?? "wallets", $"{NameOf(options)}.pem");

    private static string? Password(CommandLine options)
    {
        // A bare --password would otherwise silently become no password at all.
        if (options.Has("password") && options.Get("password") is null)
            throw new CliUsageException("--password needs a value, for example: --password \"correct horse battery staple\"");
        if (options.Get("password") is { Length: 0 })
            throw new CliUsageException("The password cannot be empty.");

        var fromEnvironment = Environment.GetEnvironmentVariable("MINICHAIN_WALLET_PASSWORD");
        return options.Get("password") ?? (string.IsNullOrEmpty(fromEnvironment) ? null : fromEnvironment);
    }

    // \z rather than $: in .NET, $ also matches just before a trailing newline.
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex WalletName();
}
