using System.Numerics;
using MiniChain.Core;
using MiniChain.Node.Storage;

namespace MiniChain.Node;

public enum TransactionSubmission { Added, Known, Rejected }

public enum BlockSubmission
{
    /// <summary>The block extended our tip.</summary>
    Appended,
    /// <summary>We already have the block.</summary>
    Known,
    /// <summary>The block is ahead of our tip and does not connect to it: we are behind and should sync.</summary>
    Behind,
    /// <summary>An old block, or one from a shorter side chain.</summary>
    Ignored,
    /// <summary>The block connects to our tip but is invalid.</summary>
    Rejected,
}

public enum ChainAdoption { Replaced, NotBetter, Invalid }

public sealed record TransactionResult(TransactionSubmission Status, string? Error = null);

/// <param name="TipIndex">This node's latest block index when the block was handled.</param>
public sealed record BlockResult(BlockSubmission Status, string? Error = null, long TipIndex = 0);

/// <param name="Returned">Transactions from discarded blocks that went back into the mempool.</param>
public sealed record ChainAdoptionResult(
    ChainAdoption Status,
    string? Error = null,
    long OldHeight = 0,
    long NewHeight = 0,
    IReadOnlyList<Transaction>? Returned = null);

public sealed record ChainSummary(long Height, string LatestHash, int NextDifficulty, BigInteger CumulativeWork, int PendingTransactions);

public sealed record AccountInfo(string Address, long Balance, long Nonce, long NextNonce);

/// <summary>
/// The node's copy of the chain and its mempool. <see cref="Blockchain"/> and <see cref="Mempool"/> are
/// not thread-safe, but HTTP requests, the miner and the sync service all run concurrently, so every
/// access goes through this class under a single lock. Nothing slow (mining, network calls, validating a
/// whole chain) ever runs while the lock is held.
/// </summary>
public sealed class NodeState : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Blockchain _chain;
    private readonly Mempool _mempool;
    private readonly IChainStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<NodeState> _logger;
    private CancellationTokenSource _tipChanged = new();

    public NodeState(Blockchain chain, Mempool mempool, IChainStore store, TimeProvider time, ILogger<NodeState> logger)
    {
        _chain = chain;
        _mempool = mempool;
        _store = store;
        _time = time;
        _logger = logger;
    }

    public ChainParameters Parameters => _chain.Parameters;

    public ChainSummary GetSummary()
    {
        lock (_gate)
            return new ChainSummary(_chain.LatestBlock.Index, _chain.LatestBlock.Hash, _chain.NextDifficulty, _chain.CumulativeWork, _mempool.Count);
    }

    public (IReadOnlyList<Block> Blocks, BigInteger CumulativeWork) GetChain()
    {
        lock (_gate)
            return (_chain.Blocks.ToArray(), _chain.CumulativeWork);
    }

    public Block GetLatestBlock()
    {
        lock (_gate)
            return _chain.LatestBlock;
    }

    public AccountInfo GetAccount(string address)
    {
        lock (_gate)
        {
            var nonce = _chain.GetNonce(address);
            var pending = _mempool.Transactions.Count(t => t.From == address);
            return new AccountInfo(address, _chain.GetBalance(address), nonce, nonce + pending + 1);
        }
    }

    public IReadOnlyList<Transaction> GetPendingTransactions()
    {
        lock (_gate)
            return _mempool.Transactions.ToArray();
    }

    public int PendingTransactionCount
    {
        get
        {
            lock (_gate)
                return _mempool.Count;
        }
    }

    public TransactionResult SubmitTransaction(Transaction transaction)
    {
        lock (_gate)
        {
            // Every peer forwards the transactions it accepts, so seeing one again is normal and ends the gossip loop.
            if (_mempool.Contains(transaction.Id) || _chain.ContainsTransaction(transaction.Id))
                return new TransactionResult(TransactionSubmission.Known);

            return _mempool.TryAdd(transaction, _chain, out var error)
                ? new TransactionResult(TransactionSubmission.Added)
                : new TransactionResult(TransactionSubmission.Rejected, error);
        }
    }

    /// <summary>Handles a block from a peer, or one this node mined (the plan's Phase 8, task 4).</summary>
    public BlockResult SubmitBlock(Block block)
    {
        lock (_gate)
        {
            if (_chain.ContainsBlock(block.Hash))
                return new BlockResult(BlockSubmission.Known);

            if (!ChainValidator.ValidateNotInFuture(block, Now(), _chain.Parameters, out var future))
                return new BlockResult(BlockSubmission.Rejected, $"Block #{block.Index}: {future}");

            var tip = _chain.LatestBlock;
            if (block.PreviousHash == tip.Hash)
            {
                if (!_chain.TryAddBlock(block, out var error))
                    return new BlockResult(BlockSubmission.Rejected, error);

                Persist();
                PruneMempool([]);
                SignalTipChanged();
                return new BlockResult(BlockSubmission.Appended);
            }

            if (block.Index <= tip.Index)
                return new BlockResult(BlockSubmission.Ignored);

            // A block further ahead that does not connect means the peer has blocks we have never seen
            // (this includes a competing block at our next height built on a different parent). It can only
            // be fully checked against its own chain, but it must at least carry real proof of work.
            return ChainValidator.ValidateProofOfWork(block, _chain.Parameters, out var forged)
                ? new BlockResult(BlockSubmission.Behind, TipIndex: tip.Index)
                : new BlockResult(BlockSubmission.Rejected, $"Block #{block.Index}: {forged}");
        }
    }

    /// <summary>
    /// The consensus rule: adopt <paramref name="blocks"/> only if they form a fully valid chain with more
    /// cumulative work than ours. Transactions from our discarded blocks go back to the mempool.
    /// </summary>
    public ChainAdoptionResult TryAdoptChain(IReadOnlyList<Block> blocks)
    {
        // Validating every block and signature is the slow part, so it happens before taking the lock.
        if (!ChainValidator.TryValidate(blocks, _chain.Parameters, out var candidate, out var invalid))
            return new ChainAdoptionResult(ChainAdoption.Invalid, invalid);

        var now = Now();
        for (var i = 0; i < candidate.Blocks.Count; i++)
        {
            if (!ChainValidator.ValidateNotInFuture(candidate.Blocks[i], now, _chain.Parameters, out var future))
                return new ChainAdoptionResult(ChainAdoption.Invalid, $"Block #{i}: {future}");
        }

        lock (_gate)
        {
            var oldHeight = _chain.LatestBlock.Index;
            if (!_chain.TryReplaceChain(candidate, out var orphaned, out var error))
                return new ChainAdoptionResult(ChainAdoption.NotBetter, error);

            Persist();
            PruneMempool(orphaned);
            SignalTipChanged();
            var returned = orphaned.Where(t => _mempool.Contains(t.Id)).ToArray();
            return new ChainAdoptionResult(ChainAdoption.Replaced, null, oldHeight, _chain.LatestBlock.Index, returned);
        }
    }

    /// <summary>
    /// Builds the next block to mine from the best pending transactions. The returned token is cancelled
    /// as soon as the tip changes, which makes the candidate stale.
    /// </summary>
    public (Block Candidate, CancellationToken TipChanged) CreateCandidateBlock(string minerAddress, long timestamp)
    {
        lock (_gate)
        {
            var transactions = _mempool.SelectForBlock(_chain.Parameters.MaxTransactionsPerBlock);
            return (_chain.CreateCandidateBlock(minerAddress, transactions, timestamp), _tipChanged.Token);
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _tipChanged.Dispose();
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    private void Persist()
    {
        // Saving happens under the lock so the file always matches a chain the node really had. If it fails the
        // node keeps running from memory; the next save writes the whole difference again.
        try
        {
            _store.Save(_chain.Blocks);
        }
        catch (Exception e)
        {
            Log.ChainSaveFailed(_logger, _store.Location, e);
        }
    }

    private void PruneMempool(IReadOnlyList<Transaction> returned)
    {
        var dropped = _mempool.Revalidate(_chain, returned).Count(t => !_chain.ContainsTransaction(t.Id));
        if (dropped > 0)
            Log.MempoolPruned(_logger, dropped);
    }

    private void SignalTipChanged()
    {
        // Miners link their work to the old token. The old source is not disposed: a miner may still be
        // about to link to it, and a source without a timer holds nothing that needs releasing.
        var stale = _tipChanged;
        _tipChanged = new CancellationTokenSource();
        stale.Cancel();
    }
}
