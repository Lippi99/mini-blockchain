namespace MiniChain.Core.Tests;

/// <summary>A named participant in a test. Until Phase 7 adds wallets, the address is derived from the name.</summary>
internal sealed class TestAccount(string name)
{
    public string Name { get; } = name;

    public string Address { get; } = HashUtil.Sha256Hex(name)[..40];

    public Transaction Pay(TestAccount to, long amount, long nonce, long fee = 0, long timestamp = Genesis.Timestamp) =>
        Transaction.Create(Address, to.Address, amount, fee, nonce, timestamp);

    public override string ToString() => Name;
}

internal static class TestAccounts
{
    public static readonly TestAccount Alice = new("alice");
    public static readonly TestAccount Bob = new("bob");
    public static readonly TestAccount Carol = new("carol");
    public static readonly TestAccount Miner = new("miner");
}
