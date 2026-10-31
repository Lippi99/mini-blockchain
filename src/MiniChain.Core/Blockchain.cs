using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace MiniChain.Core;

/// <summary>
/// An ordered list of blocks, each one linked to the previous block by its hash, plus the ledger
/// state (balances and nonces) those blocks produce. Not thread-safe.
/// </summary>
public sealed class Blockchain
{
    private List<Block> _blocks = [];
    private HashSet<string> _blockHashes = new(StringComparer.Ordinal);
    private LedgerState _ledger = new();

    /// <summary>Creates a chain that holds only the genesis block.</summary>
    public Blockchain(ChainParameters? parameters = null)
    {
        Parameters = parameters ?? ChainParameters.Default;
        _blocks.Add(Genesis.Create());
        _blockHashes.Add(Genesis.Hash);
        CumulativeWork = ProofOfWork.Work(0);
    }

    /// <summary>Creates a chain from blocks that already passed full validation, for example after loading them from disk.</summary>
    public Blockchain(ValidatedChain chain)
    {
        Parameters = chain.Parameters;
        Adopt(chain);
    }

    public ChainParameters Parameters { get; }

    public IReadOnlyList<Block> Blocks => _blocks;

    public Block LatestBlock => _blocks[^1];

    /// <summary>The sum of 16^difficulty over all blocks: the amount of work an attacker would have to redo.</summary>
    public BigInteger CumulativeWork { get; private set; }

    /// <summary>The difficulty the next block must have.</summary>
    public int NextDifficulty => DifficultyAdjustment.ExpectedDifficulty(_blocks, _blocks.Count, Parameters);

    /// <summary>Every address that ever held coins, with its current balance.</summary>
    public IReadOnlyDictionary<string, long> Balances => _ledger.Balances;

    public long GetBalance(string address) => _ledger.GetBalance(address);

    /// <summary>The nonce of the address's last confirmed transaction, or 0 if it never sent one.</summary>
    public long GetNonce(string address) => _ledger.GetNonce(address);

    public bool ContainsTransaction(string id) => _ledger.ContainsTransaction(id);

    public bool ContainsBlock(string hash) => _blockHashes.Contains(hash);

    /// <summary>Re-checks every block from scratch; <paramref name="error"/> says which block is broken and why.</summary>
    public bool IsValid([NotNullWhen(false)] out string? error) => ChainValidator.ValidateChain(_blocks, Parameters, out error);

    /// <summary>Validates <paramref name="block"/> as the next block (header, proof of work and transactions) and appends it.</summary>
    public bool TryAddBlock(Block block, [NotNullWhen(false)] out string? error)
    {
        if (!ChainValidator.ValidateBlock(block, LatestBlock, Parameters, NextDifficulty, out var reason) ||
            !_ledger.TryApplyBlock(block, Parameters, out reason))
        {
            error = $"Block #{block.Index}: {reason}";
            return false;
        }

        _blocks.Add(block);
        _blockHashes.Add(block.Hash);
        CumulativeWork += ProofOfWork.Work(block.Difficulty);
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
    /// The consensus rule: switch to <paramref name="candidate"/> only if it has strictly more
    /// cumulative work than this chain. On a tie the chain this node already has wins.
    /// <paramref name="orphaned"/> receives the transactions from this chain's discarded blocks that
    /// the new chain does not contain, so they can go back to the mempool.
    /// </summary>
    public bool TryReplaceChain(ValidatedChain candidate, out IReadOnlyList<Transaction> orphaned, [NotNullWhen(false)] out string? error)
    {
        orphaned = [];
        if (candidate.Parameters != Parameters)
        {
            error = "the chain was validated with different chain parameters.";
            return false;
        }
        if (candidate.CumulativeWork <= CumulativeWork)
        {
            error = $"its cumulative work ({candidate.CumulativeWork}) is not more than this chain's ({CumulativeWork}).";
            return false;
        }

        var forkIndex = 0;
        while (forkIndex < Math.Min(_blocks.Count, candidate.Blocks.Count) && _blocks[forkIndex].Hash == candidate.Blocks[forkIndex].Hash)
            forkIndex++;

        orphaned = _blocks
            .Skip(forkIndex)
            .SelectMany(block => block.Transactions.Skip(1)) // Coinbases die with their block.
            .Where(transaction => !candidate.Ledger.ContainsTransaction(transaction.Id))
            .ToList();

        Adopt(candidate);
        error = null;
        return true;
    }

    /// <summary>Validates <paramref name="blocks"/> from scratch and adopts them if they have more work. See <see cref="TryReplaceChain(ValidatedChain, out IReadOnlyList{Transaction}, out string?)"/>.</summary>
    public bool TryReplaceChain(IReadOnlyList<Block> blocks, out IReadOnlyList<Transaction> orphaned, [NotNullWhen(false)] out string? error)
    {
        orphaned = [];
        if (!ChainValidator.TryValidate(blocks, Parameters, out var candidate, out var reason))
        {
            error = $"the chain is invalid: {reason}";
            return false;
        }
        return TryReplaceChain(candidate, out orphaned, out error);
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
        var reward = checked(Parameters.BlockReward + transactions.Sum(t => t.Fee));

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

    private void Adopt(ValidatedChain chain)
    {
        // The ledger is cloned so that two chains built from the same ValidatedChain never share state.
        _blocks = [.. chain.Blocks];
        _blockHashes = new HashSet<string>(_blocks.Select(block => block.Hash), StringComparer.Ordinal);
        _ledger = chain.Ledger.Clone();
        CumulativeWork = chain.CumulativeWork;
    }
}
