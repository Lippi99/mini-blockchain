namespace MiniChain.Core.Tests;

public class TransactionValidatorTests
{
    private static readonly TestAccount Alice = TestAccounts.Alice;
    private static readonly TestAccount Bob = TestAccounts.Bob;

    [Fact]
    public void A_well_formed_transfer_is_valid()
    {
        Assert.True(TransactionValidator.Validate(Alice.Pay(Bob, 10, nonce: 1, fee: 1), out var error), error);
    }

    public static TheoryData<string, Transaction, string> InvalidTransfers => new()
    {
        { "zero amount", Alice.Pay(Bob, 0, nonce: 1), "amount must be greater than zero" },
        { "negative amount", Alice.Pay(Bob, -5, nonce: 1), "amount must be greater than zero" },
        { "negative fee", Alice.Pay(Bob, 5, nonce: 1, fee: -1), "fee cannot be negative" },
        { "amount plus fee overflows", Alice.Pay(Bob, long.MaxValue, nonce: 1, fee: 1), "too large" },
        { "short sender", Transaction.Create("abc", Bob.Address, 5, 0, 1, 0), "sender is not a valid address" },
        { "uppercase sender", Transaction.Create(Alice.Address.ToUpperInvariant(), Bob.Address, 5, 0, 1, 0), "sender is not a valid address" },
        { "non-hex recipient", Transaction.Create(Alice.Address, new string('g', 40), 5, 0, 1, 0), "recipient is not a valid address" },
        { "edited after creation", Alice.Pay(Bob, 5, nonce: 1) with { Amount = 500 }, "id does not match" },
        { "coinbase in the wrong place", Transaction.CreateCoinbase(Bob.Address, 50, 1, 0), "only the first transaction" },
    };

    [Theory]
    [MemberData(nameof(InvalidTransfers))]
    public void Invalid_transfers_are_rejected(string scenario, Transaction transaction, string expectedError)
    {
        Assert.False(TransactionValidator.Validate(transaction, out var error), scenario);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void A_well_formed_coinbase_is_valid()
    {
        var coinbase = Transaction.CreateCoinbase(Bob.Address, 50, blockIndex: 3, timestamp: 0);

        Assert.True(TransactionValidator.ValidateCoinbase(coinbase, blockIndex: 3, out var error), error);
    }

    public static TheoryData<string, Transaction, string> InvalidCoinbases => new()
    {
        { "not a coinbase", Alice.Pay(Bob, 5, nonce: 3), "first transaction must be the coinbase" },
        { "bad recipient", Transaction.CreateCoinbase("nobody", 50, 3, 0), "recipient is not a valid address" },
        { "zero amount", Transaction.CreateCoinbase(Bob.Address, 0, 3, 0), "amount must be greater than zero" },
        { "pays a fee", Transaction.Create(Transaction.CoinbaseSender, Bob.Address, 50, 1, 3, 0), "pays no fee" },
        { "wrong nonce", Transaction.CreateCoinbase(Bob.Address, 50, 4, 0), "must equal the block index 3" },
        { "signed", Transaction.CreateCoinbase(Bob.Address, 50, 3, 0) with { Signature = "ab" }, "no public key or signature" },
        { "has a public key", Transaction.CreateCoinbase(Bob.Address, 50, 3, 0) with { PublicKey = "ab" }, "no public key or signature" },
        { "edited after creation", Transaction.CreateCoinbase(Bob.Address, 50, 3, 0) with { Amount = 5000 }, "id does not match" },
    };

    [Theory]
    [MemberData(nameof(InvalidCoinbases))]
    public void Invalid_coinbases_are_rejected(string scenario, Transaction coinbase, string expectedError)
    {
        Assert.False(TransactionValidator.ValidateCoinbase(coinbase, blockIndex: 3, out var error), scenario);
        Assert.Contains(expectedError, error);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456", false)]
    [InlineData("0123456789abcdef0123456789abcdef012345678", false)]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456g", false)]
    [InlineData("COINBASE", false)]
    [InlineData(null, false)]
    public void Addresses_are_40_lowercase_hex_characters(string? address, bool expected)
    {
        Assert.Equal(expected, Address.IsValid(address));
    }
}
