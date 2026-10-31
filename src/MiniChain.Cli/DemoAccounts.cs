using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>Named, in-memory wallets for the demos, plus readable names for printing addresses.</summary>
internal sealed class DemoAccounts : IDisposable
{
    private readonly Dictionary<string, Wallet> _wallets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _namesByAddress = new(StringComparer.Ordinal);

    /// <summary>The wallet called <paramref name="name"/>, created with a new key pair the first time it is used.</summary>
    public Wallet this[string name]
    {
        get
        {
            if (!_wallets.TryGetValue(name, out var wallet))
            {
                wallet = new Wallet();
                _wallets[name] = wallet;
                _namesByAddress[wallet.Address] = name;
            }
            return wallet;
        }
    }

    /// <summary>The display name for an address: the account name if known, otherwise a shortened address.</summary>
    public string NameOf(string address) =>
        _namesByAddress.TryGetValue(address, out var name) ? name
        : address.Length > 12 ? $"{address[..8]}..." : address;

    public void Dispose()
    {
        foreach (var wallet in _wallets.Values)
            wallet.Dispose();
    }
}
