namespace MiniChain.Core.Tests;

public class TransactionTests
{
    private static readonly string AddressOne = new('1', 40);
    private static readonly string AddressTwo = new('2', 40);

    private static Transaction Sample() =>
        Transaction.Create(AddressOne, AddressTwo, amount: 5, fee: 1, nonce: 1, timestamp: 1_767_225_600_000);

    [Fact]
    public void Create_fills_in_the_id_from_the_canonical_fields()
    {
        // SHA-256 of "{From}|{To}|{Amount}|{Fee}|{Nonce}|{Timestamp}|{PublicKey}" (no public key here),
        // computed with `sha256sum`.
        var transaction = Sample();

        Assert.Equal("e7875059800348132e7854f1cf602fbda77eb70b642069a2e3427dc6d1cc3b4e", transaction.Id);
        Assert.Equal(transaction.ComputeId(), transaction.Id);
    }

    public static TheoryData<string, Func<Transaction, Transaction>> FieldChanges => new()
    {
        { "From", t => t with { From = new string('9', 40) } },
        { "To", t => t with { To = new string('9', 40) } },
        { "Amount", t => t with { Amount = 6 } },
        { "Fee", t => t with { Fee = 2 } },
        { "Nonce", t => t with { Nonce = 2 } },
        { "Timestamp", t => t with { Timestamp = 1 } },
    };

    [Theory]
    [MemberData(nameof(FieldChanges))]
    public void Changing_any_field_changes_the_id(string field, Func<Transaction, Transaction> change)
    {
        var original = Sample();

        Assert.True(original.ComputeId() != change(original).ComputeId(), $"Changing {field} did not change the id.");
    }

    [Fact]
    public void The_signature_is_not_part_of_the_id()
    {
        var original = Sample();

        Assert.Equal(original.ComputeId(), (original with { Signature = "abcd" }).ComputeId());
    }

    [Fact]
    public void A_coinbase_pays_no_fee_and_uses_the_block_index_as_its_nonce()
    {
        var coinbase = Transaction.CreateCoinbase(AddressOne, amount: 50, blockIndex: 7, timestamp: 123);

        Assert.True(coinbase.IsCoinbase);
        Assert.Equal(Transaction.CoinbaseSender, coinbase.From);
        Assert.Equal(AddressOne, coinbase.To);
        Assert.Equal(0, coinbase.Fee);
        Assert.Equal(7, coinbase.Nonce);
        Assert.Equal(coinbase.ComputeId(), coinbase.Id);
    }

    [Fact]
    public void Identical_rewards_in_different_blocks_get_different_ids()
    {
        var first = Transaction.CreateCoinbase(AddressOne, amount: 50, blockIndex: 1, timestamp: 123);
        var second = Transaction.CreateCoinbase(AddressOne, amount: 50, blockIndex: 2, timestamp: 123);

        Assert.NotEqual(first.Id, second.Id);
    }
}
