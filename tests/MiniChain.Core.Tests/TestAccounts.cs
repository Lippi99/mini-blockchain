namespace MiniChain.Core.Tests;

/// <summary>A named participant in a test, backed by a real wallet so its payments are signed.</summary>
internal sealed class TestAccount(string name)
{
    public string Name { get; } = name;

    public Wallet Wallet { get; } = new();

    public string Address => Wallet.Address;

    public Transaction Pay(TestAccount to, long amount, long nonce, long fee = 0, long timestamp = Genesis.Timestamp) =>
        Wallet.CreateTransaction(to.Address, amount, fee, nonce, timestamp);

    public override string ToString() => Name;
}

internal static class TestAccounts
{
    public static readonly TestAccount Alice = new("alice");
    public static readonly TestAccount Bob = new("bob");
    public static readonly TestAccount Carol = new("carol");
    public static readonly TestAccount Mallory = new("mallory");
    public static readonly TestAccount Miner = new("miner");
}
