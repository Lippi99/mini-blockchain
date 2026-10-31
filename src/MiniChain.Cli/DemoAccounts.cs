using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>
/// Named accounts for the demos. Until wallets arrive in Phase 7, an address is simply the first 40
/// hex digits of SHA-256(name), and nothing stops anyone from spending from it.
/// </summary>
internal sealed class DemoAccounts
{
    private readonly Dictionary<string, string> _namesByAddress = new(StringComparer.Ordinal);

    /// <summary>The address for <paramref name="name"/>.</summary>
    public string this[string name]
    {
        get
        {
            var address = HashUtil.Sha256Hex(name)[..Address.Length];
            _namesByAddress[address] = name;
            return address;
        }
    }

    /// <summary>The display name for an address: the account name if known, otherwise a shortened address.</summary>
    public string NameOf(string address) =>
        _namesByAddress.TryGetValue(address, out var name) ? name
        : address.Length > 12 ? $"{address[..8]}..." : address;
}
