namespace MiniChain.Core.Tests;

public class DifficultyAdjustmentTests
{
    private static readonly ChainParameters Adjusting = new()
    {
        Difficulty = 3,
        AdjustDifficulty = true,
        AdjustmentInterval = 5,
        TargetBlockTimeMs = 1000,
    };

    /// <summary>Genesis plus blocks <paramref name="gapMs"/> apart. Only timestamps and difficulty matter here.</summary>
    private static List<Block> ChainWithGaps(int blocks, long gapMs, int difficulty = 3)
    {
        var chain = new List<Block> { Genesis.Create() };
        for (var i = 1; i <= blocks; i++)
            chain.Add(new Block { Index = i, Timestamp = Genesis.Timestamp + i * gapMs, Difficulty = difficulty });
        return chain;
    }

    [Fact]
    public void Without_adjustment_the_difficulty_never_changes()
    {
        var chain = ChainWithGaps(30, gapMs: 1);
        var parameters = Adjusting with { AdjustDifficulty = false };

        for (var index = 1; index <= chain.Count; index++)
            Assert.Equal(3, DifficultyAdjustment.ExpectedDifficulty(chain, index, parameters));
    }

    [Fact]
    public void The_first_period_uses_the_starting_difficulty()
    {
        var chain = ChainWithGaps(5, gapMs: 1);

        for (var index = 1; index <= Adjusting.AdjustmentInterval; index++)
            Assert.Equal(3, DifficultyAdjustment.ExpectedDifficulty(chain, index, Adjusting));
    }

    [Theory]
    [InlineData(100, 4)]   // Much faster than the 1 s target: harder.
    [InlineData(1000, 3)]  // On target: unchanged.
    [InlineData(1900, 3)]  // Slower, but within 2x: unchanged.
    [InlineData(5000, 2)]  // Much slower: easier.
    public void Every_interval_the_difficulty_follows_the_block_time(long gapMs, int expected)
    {
        var chain = ChainWithGaps(5, gapMs);

        Assert.Equal(expected, DifficultyAdjustment.ExpectedDifficulty(chain, 6, Adjusting));
    }

    [Fact]
    public void Between_retargets_the_previous_difficulty_is_kept()
    {
        var chain = ChainWithGaps(6, gapMs: 100);
        chain[6].Difficulty = 4;

        Assert.Equal(4, DifficultyAdjustment.ExpectedDifficulty(chain, 7, Adjusting));
    }

    [Fact]
    public void The_difficulty_never_drops_below_one()
    {
        var chain = ChainWithGaps(5, gapMs: 60_000, difficulty: 1);

        Assert.Equal(1, DifficultyAdjustment.ExpectedDifficulty(chain, 6, Adjusting with { Difficulty = 1 }));
    }

    [Fact]
    public void A_chain_mined_with_adjustment_validates_and_a_skipped_retarget_is_rejected()
    {
        var parameters = new ChainParameters
        {
            Difficulty = 1,
            AdjustDifficulty = true,
            AdjustmentInterval = 2,
            TargetBlockTimeMs = 1000,
        };
        var chain = new Blockchain(parameters);
        for (var i = 1; i <= 5; i++)
            chain.AddBlock(new Block { Timestamp = Genesis.Timestamp + i, Transactions = [$"fast block {i}"] });

        Assert.True(chain.IsValid(out var error), error);
        Assert.Equal([1, 1, 2, 2, 3], chain.Blocks.Skip(1).Select(b => b.Difficulty));

        var cheap = TestChains.NextBlock(chain.Blocks.Take(3).ToList(), difficulty: 1, "ignores the retarget");
        Miner.MineSequential(cheap);
        Assert.False(ChainValidator.ValidateChain([.. chain.Blocks.Take(3), cheap], parameters, out error));
        Assert.Equal("Block #3: the difficulty is 1, but the network expects 2.", error);
    }

    [Fact]
    public void Invalid_parameters_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainParameters { Difficulty = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainParameters { Difficulty = ProofOfWork.MaxDifficulty + 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainParameters { AdjustmentInterval = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainParameters { TargetBlockTimeMs = 0 });
    }
}
