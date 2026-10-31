namespace MiniChain.Core.Tests;

public class MinerTests
{
    private static Block Candidate(int difficulty, int sample = 0) => new()
    {
        Index = 1,
        Timestamp = Genesis.Timestamp + 1,
        PreviousHash = Genesis.Hash,
        Difficulty = difficulty,
        Transactions = [Transaction.CreateCoinbase(TestAccounts.Miner.Address, 50, blockIndex: 1, Genesis.Timestamp + sample)],
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Mine_finds_a_hash_with_enough_leading_zeros(int difficulty)
    {
        var block = Miner.Mine(Candidate(difficulty));

        Assert.StartsWith(new string('0', difficulty), block.Hash);
        Assert.Equal(block.ComputeHash(), block.Hash);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_optimized_and_parallel_miners_agree_with_the_naive_miner(int difficulty)
    {
        for (var sample = 0; sample < 5; sample++)
        {
            var naive = Miner.Mine(Candidate(difficulty, sample));
            var sequential = Miner.MineSequential(Candidate(difficulty, sample));
            var parallel = Miner.MineParallel(Candidate(difficulty, sample));

            Assert.Equal(naive.Nonce, sequential.Nonce);
            Assert.Equal(naive.Hash, sequential.Hash);
            Assert.Equal(naive.Nonce, parallel.Nonce);
            Assert.Equal(naive.Hash, parallel.Hash);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(16)]
    public void The_parallel_miner_finds_the_smallest_nonce_with_any_number_of_workers(int workers)
    {
        var expected = Miner.MineSequential(Candidate(3)).Nonce;

        var block = Miner.MineParallel(Candidate(3), workers);

        Assert.Equal(expected, block.Nonce);
        Assert.Equal(block.ComputeHash(), block.Hash);
    }

    [Fact]
    public void Mining_only_changes_the_nonce_and_the_hash()
    {
        var block = Candidate(2);
        var transactions = block.Transactions;

        Miner.MineParallel(block);

        Assert.Equal(1, block.Index);
        Assert.Equal(Genesis.Timestamp + 1, block.Timestamp);
        Assert.Equal(Genesis.Hash, block.PreviousHash);
        Assert.Equal(2, block.Difficulty);
        Assert.Same(transactions, block.Transactions);
    }

    [Fact]
    public void Sequential_mining_can_be_cancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.Throws<OperationCanceledException>(
            () => Miner.MineSequential(Candidate(ProofOfWork.MaxDifficulty), cancellation.Token));
    }

    [Fact]
    public void Parallel_mining_can_be_cancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.Throws<OperationCanceledException>(
            () => Miner.MineParallel(Candidate(ProofOfWork.MaxDifficulty), cancellationToken: cancellation.Token));
    }
}
