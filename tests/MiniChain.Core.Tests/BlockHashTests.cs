using System.Globalization;

namespace MiniChain.Core.Tests;

public class BlockHashTests
{
    private static readonly string AddressOne = new('1', 40);
    private static readonly string AddressTwo = new('2', 40);
    private static readonly string AddressThree = new('3', 40);

    private static Block SampleBlock() => new()
    {
        Index = 1,
        Timestamp = 1_767_225_600_000,
        PreviousHash = "abc123",
        Transactions =
        [
            Transaction.Create(AddressOne, AddressTwo, amount: 5, fee: 1, nonce: 1, timestamp: 1_767_225_600_000),
            Transaction.Create(AddressTwo, AddressThree, amount: 2, fee: 0, nonce: 1, timestamp: 1_767_225_600_001),
        ],
        Difficulty = 2,
        Nonce = 42,
    };

    [Fact]
    public void The_same_block_always_produces_the_same_hash()
    {
        var block = SampleBlock();

        Assert.Equal(block.ComputeHash(), block.ComputeHash());
        Assert.Equal(SampleBlock().ComputeHash(), block.ComputeHash());
    }

    [Fact]
    public void The_hash_matches_the_canonical_header_format()
    {
        // Computed outside .NET with `sha256sum`. Each transaction contributes
        // SHA-256("{recomputed id}|{stored id}|{signature}"), TransactionsHash is the SHA-256 of those
        // hashes concatenated, and the block hash is the SHA-256 of
        // "1|1767225600000|abc123|{TransactionsHash}|2|42". If this test fails, the format changed and
        // every existing chain would stop validating.
        var block = SampleBlock();

        Assert.Equal("9bb8a249cad0565dd1deae11dd0ad85273a2106e0f8ba36c3daee844018aabf0", block.ComputeTransactionsHash());
        Assert.Equal("ccd22c1b5f3bdd352d163623ffa9856ae80ba5edc5b14121f2ba7141468be23b", block.ComputeHash());
    }

    public static TheoryData<string, Action<Block>> FieldChanges => new()
    {
        { "Index", b => b.Index++ },
        { "Timestamp", b => b.Timestamp++ },
        { "PreviousHash", b => b.PreviousHash = "abc124" },
        { "Transaction amount", b => b.Transactions = [b.Transactions[0] with { Amount = 6 }, b.Transactions[1]] },
        { "Transaction signature", b => b.Transactions = [b.Transactions[0] with { Signature = "ab" }, b.Transactions[1]] },
        { "Transaction stored id", b => b.Transactions = [b.Transactions[0] with { Id = new string('0', 64) }, b.Transactions[1]] },
        { "Transaction order", b => b.Transactions = [b.Transactions[1], b.Transactions[0]] },
        { "Transaction removed", b => b.Transactions = [b.Transactions[0]] },
        { "Transaction added", b => b.Transactions = [.. b.Transactions, Transaction.Create(AddressThree, AddressOne, 1, 0, 1, 0)] },
        { "Difficulty", b => b.Difficulty++ },
        { "Nonce", b => b.Nonce++ },
    };

    [Theory]
    [MemberData(nameof(FieldChanges))]
    public void Changing_any_single_field_changes_the_hash(string field, Action<Block> change)
    {
        var block = SampleBlock();
        var before = block.ComputeHash();

        change(block);

        Assert.True(before != block.ComputeHash(), $"Changing {field} did not change the hash.");
    }

    [Fact]
    public void A_transaction_edited_without_updating_its_stored_id_still_changes_the_hash()
    {
        // The block hash uses each transaction's recomputed id, not the stored one.
        var block = SampleBlock();
        var before = block.ComputeHash();

        block.Transactions = [block.Transactions[0] with { Amount = 500 }, block.Transactions[1]];

        Assert.Equal(SampleBlock().Transactions[0].Id, block.Transactions[0].Id);
        Assert.NotEqual(before, block.ComputeHash());
    }

    [Fact]
    public void The_stored_hash_is_not_part_of_the_hashed_header()
    {
        var block = SampleBlock();
        var before = block.ComputeHash();

        block.Hash = "anything";

        Assert.Equal(before, block.ComputeHash());
    }

    [Fact]
    public void The_hash_does_not_depend_on_the_current_culture()
    {
        var block = SampleBlock();
        block.Nonce = -42; // Negative numbers are formatted differently by some cultures.
        var invariantHash = block.ComputeHash();

        var original = CultureInfo.CurrentCulture;
        var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        odd.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = odd;
            Assert.Equal(invariantHash, block.ComputeHash());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
