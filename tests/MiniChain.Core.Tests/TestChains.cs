namespace MiniChain.Core.Tests;

/// <summary>Builds small chains at a low difficulty so the test suite stays fast.</summary>
internal static class TestChains
{
    public static Blockchain Create(int blocks = 0, int difficulty = 1)
    {
        var chain = new Blockchain(new ChainParameters { Difficulty = difficulty });
        for (var i = 1; i <= blocks; i++)
            chain.AddBlock(new Block { Timestamp = Genesis.Timestamp + i * 1000, Transactions = [$"payment {i}"] });
        return chain;
    }

    /// <summary>An unmined block that would come right after the last block of <paramref name="blocks"/>.</summary>
    public static Block NextBlock(IReadOnlyList<Block> blocks, int difficulty, params string[] transactions) => new()
    {
        Index = blocks[^1].Index + 1,
        Timestamp = blocks[^1].Timestamp + 1000,
        PreviousHash = blocks[^1].Hash,
        Difficulty = difficulty,
        Transactions = transactions,
    };

    /// <summary>Gives <paramref name="block"/> a correct hash that does NOT meet its difficulty.</summary>
    public static void HashWithoutMining(Block block)
    {
        block.Hash = block.ComputeHash();
        while (ProofOfWork.MeetsDifficulty(block.Hash, block.Difficulty))
        {
            block.Nonce++;
            block.Hash = block.ComputeHash();
        }
    }
}
