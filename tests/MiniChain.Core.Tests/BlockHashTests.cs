using System.Globalization;

namespace MiniChain.Core.Tests;

public class BlockHashTests
{
    private static Block SampleBlock() => new()
    {
        Index = 1,
        Timestamp = 1_767_225_600_000,
        PreviousHash = "abc123",
        Transactions = ["alice pays bob 5", "bob pays carol 2"],
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
        // Computed outside .NET with `sha256sum` over
        // "1|1767225600000|abc123|{TransactionsHash}|2|42", where TransactionsHash is the
        // SHA-256 of the concatenated SHA-256 hex of each transaction. If this test fails, the
        // header format changed and every existing chain would stop validating.
        var block = SampleBlock();

        Assert.Equal("bd44ad56c327185683b371fca94b1dacec2e1037826df907d456b36e92a9a42d", block.ComputeTransactionsHash());
        Assert.Equal("59383c77ce555b1544a6ff339df88d98c5f841ac7e7cc429def453c7e4bafe24", block.ComputeHash());
    }

    public static TheoryData<string, Action<Block>> FieldChanges => new()
    {
        { "Index", b => b.Index++ },
        { "Timestamp", b => b.Timestamp++ },
        { "PreviousHash", b => b.PreviousHash = "abc124" },
        { "Transaction content", b => b.Transactions = ["alice pays bob 6", "bob pays carol 2"] },
        { "Transaction order", b => b.Transactions = ["bob pays carol 2", "alice pays bob 5"] },
        { "Transaction added", b => b.Transactions = [.. b.Transactions, "carol pays dave 1"] },
        { "Transaction removed", b => b.Transactions = ["alice pays bob 5"] },
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
    public void The_stored_hash_is_not_part_of_the_hashed_header()
    {
        var block = SampleBlock();
        var before = block.ComputeHash();

        block.Hash = "anything";

        Assert.Equal(before, block.ComputeHash());
    }

    [Fact]
    public void Joining_transactions_cannot_collide_with_a_single_transaction()
    {
        var two = SampleBlock();
        two.Transactions = ["a", "b"];
        var one = SampleBlock();
        one.Transactions = ["ab"];

        Assert.NotEqual(one.ComputeHash(), two.ComputeHash());
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
