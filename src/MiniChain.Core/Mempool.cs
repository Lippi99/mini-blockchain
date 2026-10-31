using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>
/// Valid transactions waiting to be mined. Each one is checked against the confirmed balances and
/// nonces plus the sender's other pending transactions, so for every sender the mempool holds a
/// gap-free run of nonces that they can afford. Not thread-safe.
/// </summary>
public sealed class Mempool
{
    private readonly Dictionary<string, Transaction> _transactions = new(StringComparer.Ordinal);

    // Highest fee first. Ties go to the older transaction, then to the smaller id, so the order is deterministic.
    private static readonly Comparer<Transaction> HighestFeeFirst = Comparer<Transaction>.Create((a, b) =>
    {
        var byFee = b.Fee.CompareTo(a.Fee);
        if (byFee != 0)
            return byFee;
        var byTime = a.Timestamp.CompareTo(b.Timestamp);
        return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
    });

    public int Count => _transactions.Count;

    public IReadOnlyCollection<Transaction> Transactions => _transactions.Values;

    public bool Contains(string id) => _transactions.ContainsKey(id);

    /// <summary>Adds <paramref name="transaction"/> if it is valid on top of <paramref name="chain"/> and the pending transactions.</summary>
    public bool TryAdd(Transaction transaction, Blockchain chain, [NotNullWhen(false)] out string? error)
    {
        // The cheap duplicate checks come first: a node hears about the same transaction from every peer.
        if (Contains(transaction.Id))
        {
            error = "the transaction is already pending.";
            return false;
        }
        if (chain.ContainsTransaction(transaction.Id))
        {
            error = "the transaction is already in the chain.";
            return false;
        }
        if (!TransactionValidator.Validate(transaction, out error))
            return false;

        var pending = _transactions.Values.Where(t => t.From == transaction.From).ToList();
        var expectedNonce = chain.GetNonce(transaction.From) + pending.Count + 1;
        if (transaction.Nonce != expectedNonce)
        {
            error = $"the nonce is {transaction.Nonce}, but the sender's next nonce is {expectedNonce}.";
            return false;
        }

        var available = chain.GetBalance(transaction.From) - pending.Sum(t => t.Amount + t.Fee);
        var cost = transaction.Amount + transaction.Fee;
        if (cost > available)
        {
            error = $"the transaction spends {cost}, but the sender only has {available} available.";
            return false;
        }

        _transactions.Add(transaction.Id, transaction);
        return true;
    }

    /// <summary>
    /// Picks up to <paramref name="maxCount"/> transactions for the next block, highest fee first. A
    /// sender's transactions always stay in nonce order: a high-fee second transaction waits for the
    /// sender's first one.
    /// </summary>
    public IReadOnlyList<Transaction> SelectForBlock(int maxCount)
    {
        var ready = new PriorityQueue<Queue<Transaction>, Transaction>(HighestFeeFirst);
        foreach (var sender in _transactions.Values.GroupBy(t => t.From, StringComparer.Ordinal))
        {
            var queue = new Queue<Transaction>(sender.OrderBy(t => t.Nonce));
            ready.Enqueue(queue, queue.Peek());
        }

        var selected = new List<Transaction>();
        while (selected.Count < maxCount && ready.TryDequeue(out var queue, out _))
        {
            selected.Add(queue.Dequeue());
            if (queue.Count > 0)
                ready.Enqueue(queue, queue.Peek());
        }
        return selected;
    }

    /// <summary>
    /// Brings the mempool in line with a changed chain. Transactions that were confirmed, or that no
    /// longer fit (for example because the chain now holds a different transaction with the same
    /// nonce), are dropped. <paramref name="returned"/> transactions, taken from blocks that left the
    /// chain, are added back if they are still valid. Returns everything that did not stay.
    /// </summary>
    public IReadOnlyList<Transaction> Revalidate(Blockchain chain, IEnumerable<Transaction>? returned = null)
    {
        var candidates = _transactions.Values
            .Concat(returned ?? [])
            .DistinctBy(t => t.Id)
            .OrderBy(t => t.From, StringComparer.Ordinal)
            .ThenBy(t => t.Nonce)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        _transactions.Clear();
        var removed = new List<Transaction>();
        foreach (var transaction in candidates)
        {
            if (!TryAdd(transaction, chain, out _))
                removed.Add(transaction);
        }
        return removed;
    }
}
