using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>
/// An ordered list of blocks, each one linked to the previous block by its hash, plus the ledger
/// state (balances and nonces) those blocks produce. Not thread-safe.
/// </summary>
public sealed class Blockchain(ChainParameters? parameters = null)
{
    private readonly List<Block> _blocks = [Genesis.Create()];
    private readonly LedgerState _ledger = new();

    public ChainParameters Parameters { get; } = parameters ?? ChainParameters.Default;

    public IReadOnlyList<Block> Blocks => _blocks;

    public Block LatestBlock => _blocks[^1];

    /// <summary>The difficulty the next block must have.</summary>
    public int NextDifficulty => DifficultyAdjustment.ExpectedDifficulty(_blocks, _blocks.Count, Parameters);

    /// <summary>Every address that ever held coins, with its current balance.</summary>
    public IReadOnlyDictionary<string, long> Balances => _ledger.Balances;

    public long GetBalance(string address) => _ledger.GetBalance(address);

    /// <summary>The nonce of the address's last confirmed transaction, or 0 if it never sent one.</summary>
    public long GetNonce(string address) => _ledger.GetNonce(address);

    public bool ContainsTransaction(string id) => _ledger.ContainsTransaction(id);

    /// <summary>Re-checks every block from scratch; <paramref name="error"/> says which block is broken and why.</summary>
    public bool IsValid([NotNullWhen(false)] out string? error) => ChainValidator.ValidateChain(_blocks, Parameters, out error);

    /// <summary>Validates <paramref name="block"/> as the next block (header, proof of work and transactions) and appends it.</summary>
    public bool TryAddBlock(Block block, [NotNullWhen(false)] out string? error)
    {
        if (!ChainValidator.ValidateBlock(block, LatestBlock, NextDifficulty, out var reason) ||
            !_ledger.TryApplyBlock(block, Parameters, out reason))
        {
            error = $"Block #{block.Index}: {reason}";
            return false;
        }

        _blocks.Add(block);
        error = null;
        return true;
    }

    /// <summary>Links <paramref name="block"/> to the current tip, mines it and appends it. Throws if the block is invalid.</summary>
    public void AddBlock(Block block)
    {
        block.Index = LatestBlock.Index + 1;
        block.PreviousHash = LatestBlock.Hash;
        block.Difficulty = NextDifficulty;
        Miner.MineParallel(block);
        if (!TryAddBlock(block, out var error))
            throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Builds the next block, ready to be mined: a coinbase that pays the block reward plus all fees
    /// to <paramref name="minerAddress"/>, followed by <paramref name="transactions"/>.
    /// </summary>
    public Block CreateCandidateBlock(string minerAddress, IReadOnlyList<Transaction> transactions, long timestamp)
    {
        var index = LatestBlock.Index + 1;

        // Clocks differ between machines, but a block may never be older than the block before it.
        timestamp = Math.Max(timestamp, LatestBlock.Timestamp);
        var reward = Parameters.BlockReward + transactions.Sum(t => t.Fee);

        return new Block
        {
            Index = index,
            Timestamp = timestamp,
            PreviousHash = LatestBlock.Hash,
            Difficulty = NextDifficulty,
            Transactions = [Transaction.CreateCoinbase(minerAddress, reward, index, timestamp), .. transactions],
        };
    }

    /// <summary>
    /// Mines the mempool's best transactions into the next block, rewards <paramref name="minerAddress"/>
    /// and removes the confirmed transactions from the mempool.
    /// </summary>
    public Block MinePendingTransactions(Mempool mempool, string minerAddress, long? timestamp = null)
    {
        var candidate = CreateCandidateBlock(
            minerAddress,
            mempool.SelectForBlock(Parameters.MaxTransactionsPerBlock),
            timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Miner.MineParallel(candidate);
        if (!TryAddBlock(candidate, out var error))
            throw new InvalidOperationException(error);

        mempool.Revalidate(this);
        return candidate;
    }
}
