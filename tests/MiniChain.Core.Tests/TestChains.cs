namespace MiniChain.Core.Tests;

/// <summary>Builds small chains at a low difficulty so the test suite stays fast.</summary>
internal static class TestChains
{
    /// <summary>A chain of <paramref name="blocks"/> mined blocks that only pay the test miner.</summary>
    public static Blockchain Create(int blocks = 0, int difficulty = 1) =>
        Create(new ChainParameters { Difficulty = difficulty }, blocks);

    public static Blockchain Create(ChainParameters parameters, int blocks = 0)
    {
        var chain = new Blockchain(parameters);
        for (var i = 0; i < blocks; i++)
            MineBlock(chain, TestAccounts.Miner);
        return chain;
    }

    /// <summary>
    /// Mines and appends the next block: a coinbase paying <paramref name="miner"/> the reward plus
    /// fees, followed by <paramref name="transactions"/>. Throws if the block is invalid.
    /// </summary>
    public static Block MineBlock(Blockchain chain, TestAccount miner, params Transaction[] transactions) =>
        MineBlockAt(chain, chain.LatestBlock.Timestamp + 1000, miner, transactions);

    public static Block MineBlockAt(Blockchain chain, long timestamp, TestAccount miner, params Transaction[] transactions)
    {
        var block = chain.CreateCandidateBlock(miner.Address, transactions, timestamp);
        if (!TryMineAndAdd(chain, block, out var error))
            throw new InvalidOperationException(error);
        return block;
    }

    /// <summary>Mines <paramref name="block"/> as it is and tries to append it.</summary>
    public static bool TryMineAndAdd(Blockchain chain, Block block, out string? error)
    {
        Miner.MineSequential(block);
        return chain.TryAddBlock(block, out error);
    }

    /// <summary>An unmined block, holding only a coinbase, that would follow the last of <paramref name="blocks"/>.</summary>
    public static Block NextBlock(IReadOnlyList<Block> blocks, int difficulty)
    {
        var index = blocks[^1].Index + 1;
        var timestamp = blocks[^1].Timestamp + 1000;
        return new Block
        {
            Index = index,
            Timestamp = timestamp,
            PreviousHash = blocks[^1].Hash,
            Difficulty = difficulty,
            Transactions = [Transaction.CreateCoinbase(TestAccounts.Miner.Address, 50, index, timestamp)],
        };
    }

    /// <summary>Replaces the coinbase of <paramref name="block"/> with one paying <paramref name="amount"/>, without re-mining.</summary>
    public static void ChangeReward(Block block, long amount)
    {
        var coinbase = block.Transactions[0];
        block.Transactions = [Transaction.CreateCoinbase(coinbase.To, amount, coinbase.Nonce, coinbase.Timestamp), .. block.Transactions.Skip(1)];
    }

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
