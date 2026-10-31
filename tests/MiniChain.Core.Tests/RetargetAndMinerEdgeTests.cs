namespace MiniChain.Core.Tests;

/// <summary>Edges of difficulty retargeting, and the "smallest nonce" promise of the miners.</summary>
public class RetargetAndMinerEdgeTests
{
    private static List<Block> ChainWithGaps(int blocks, long gapMs, int difficulty)
    {
        var chain = new List<Block> { Genesis.Create() };
        for (var i = 1; i <= blocks; i++)
            chain.Add(new Block { Index = i, Timestamp = Genesis.Timestamp + i * gapMs, Difficulty = difficulty });
        return chain;
    }

    private static readonly ChainParameters Adjusting = new()
    {
        Difficulty = 3,
        AdjustDifficulty = true,
        AdjustmentInterval = 5,
        TargetBlockTimeMs = 1000,
    };

    [Fact]
    public void A_retarget_starts_from_the_previous_difficulty_not_the_starting_one()
    {
        var chain = ChainWithGaps(5, gapMs: 1000, difficulty: 7);

        Assert.Equal(7, DifficultyAdjustment.ExpectedDifficulty(chain, 6, Adjusting));
    }

    [Theory]
    [InlineData(500, 3)]   // Exactly half the target (4 gaps x 500 ms = 2000 of 4000): not strictly faster, unchanged.
    [InlineData(499, 4)]
    [InlineData(2000, 3)]  // Exactly double: unchanged.
    [InlineData(2001, 2)]
    public void The_band_edges_are_exclusive(long gapMs, int expected)
    {
        var chain = ChainWithGaps(5, gapMs, difficulty: 3);

        Assert.Equal(expected, DifficultyAdjustment.ExpectedDifficulty(chain, 6, Adjusting));
    }

    [Fact]
    public void An_odd_target_is_not_rounded_down()
    {
        // Interval 2 measures one gap; the target is 3 ms, so 1 ms is faster than half of it (1.5 ms).
        var parameters = new ChainParameters { Difficulty = 1, AdjustDifficulty = true, AdjustmentInterval = 2, TargetBlockTimeMs = 3 };
        var chain = ChainWithGaps(2, gapMs: 1, difficulty: 1);

        Assert.Equal(2, DifficultyAdjustment.ExpectedDifficulty(chain, 3, parameters));
    }

    [Fact]
    public void The_difficulty_never_rises_above_64()
    {
        var chain = ChainWithGaps(5, gapMs: 1, difficulty: ProofOfWork.MaxDifficulty);

        Assert.Equal(ProofOfWork.MaxDifficulty, DifficultyAdjustment.ExpectedDifficulty(chain, 6, Adjusting with { Difficulty = 64 }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_miner_returns_the_smallest_valid_nonce(int difficulty)
    {
        for (var sample = 0; sample < 10; sample++)
        {
            var candidate = new Block
            {
                Index = 1,
                Timestamp = Genesis.Timestamp + sample,
                PreviousHash = Genesis.Hash,
                Difficulty = difficulty,
                Transactions = [Transaction.CreateCoinbase(TestAccounts.Miner.Address, 50, 1, Genesis.Timestamp + sample)],
            };
            var nonce = Miner.MineParallel(candidate, workerCount: 4).Nonce;

            // Every smaller nonce must fail: that is what "smallest" means.
            for (long smaller = 0; smaller < nonce; smaller++)
            {
                candidate.Nonce = smaller;
                Assert.False(ProofOfWork.MeetsDifficulty(candidate.ComputeHash(), difficulty), $"Nonce {smaller} was valid but {nonce} was returned.");
            }
            candidate.Nonce = nonce;
            Assert.True(ProofOfWork.MeetsDifficulty(candidate.ComputeHash(), difficulty));
        }
    }
}
