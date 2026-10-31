using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>
/// The balances, nonces and transaction ids that result from replaying a chain block by block.
/// Keeping this cache means a node never has to replay the whole chain to answer "how much does this
/// address own?".
/// </summary>
public sealed class LedgerState
{
    private readonly Dictionary<string, long> _balances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _nonces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _transactionIds = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, long> Balances => _balances;

    public long GetBalance(string address) => _balances.GetValueOrDefault(address);

    /// <summary>The nonce of the address's last confirmed transaction, or 0 if it never sent one.</summary>
    public long GetNonce(string address) => _nonces.GetValueOrDefault(address);

    public bool ContainsTransaction(string id) => _transactionIds.Contains(id);

    internal LedgerState Clone()
    {
        var clone = new LedgerState();
        foreach (var (address, balance) in _balances)
            clone._balances[address] = balance;
        foreach (var (address, nonce) in _nonces)
            clone._nonces[address] = nonce;
        clone._transactionIds.UnionWith(_transactionIds);
        return clone;
    }

    /// <summary>
    /// Checks every transaction in <paramref name="block"/> against this state and applies them, all
    /// or nothing: if any rule fails, the state is left untouched.
    /// </summary>
    internal bool TryApplyBlock(Block block, ChainParameters parameters, [NotNullWhen(false)] out string? error)
    {
        var transactions = block.Transactions;
        if (transactions.Count == 0)
        {
            error = "the block has no coinbase transaction.";
            return false;
        }
        if (transactions.Count - 1 > parameters.MaxTransactionsPerBlock)
        {
            error = $"the block has {transactions.Count - 1} transactions besides the coinbase; the limit is {parameters.MaxTransactionsPerBlock}.";
            return false;
        }

        var coinbase = transactions[0];
        if (!TransactionValidator.ValidateCoinbase(coinbase, block.Index, out var coinbaseError))
        {
            error = $"transaction #0 ({coinbase.Id}): {coinbaseError}";
            return false;
        }
        if (ContainsTransaction(coinbase.Id))
        {
            error = $"transaction #0 ({coinbase.Id}) is already in the chain.";
            return false;
        }

        // Changes are collected here first and only written to the state once the whole block is valid.
        var balances = new Dictionary<string, long>(StringComparer.Ordinal);
        var nonces = new Dictionary<string, long>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal) { coinbase.Id };
        long fees = 0;

        for (var i = 1; i < transactions.Count; i++)
        {
            var transaction = transactions[i];
            if (!TransactionValidator.Validate(transaction, out var reason))
            {
                error = $"transaction #{i} ({transaction.Id}): {reason}";
                return false;
            }
            if (ContainsTransaction(transaction.Id) || !ids.Add(transaction.Id))
            {
                error = $"transaction #{i} ({transaction.Id}) is a duplicate.";
                return false;
            }

            var expectedNonce = (nonces.TryGetValue(transaction.From, out var lastNonce) ? lastNonce : GetNonce(transaction.From)) + 1;
            if (transaction.Nonce != expectedNonce)
            {
                error = $"transaction #{i} ({transaction.Id}) has nonce {transaction.Nonce}, but the sender's next nonce is {expectedNonce}.";
                return false;
            }

            var balance = BalanceOf(transaction.From);
            var cost = transaction.Amount + transaction.Fee;
            if (balance < cost)
            {
                error = $"transaction #{i} ({transaction.Id}) spends {cost}, but the sender only has {balance}.";
                return false;
            }

            balances[transaction.From] = balance - cost;
            balances[transaction.To] = BalanceOf(transaction.To) + transaction.Amount;
            nonces[transaction.From] = transaction.Nonce;
            fees += transaction.Fee;
        }

        if (coinbase.Amount > parameters.BlockReward + fees)
        {
            error = $"the coinbase pays {coinbase.Amount}, but the block reward plus fees is only {parameters.BlockReward + fees}.";
            return false;
        }

        // The reward is credited last, so a miner can only spend it from the next block on.
        balances[coinbase.To] = BalanceOf(coinbase.To) + coinbase.Amount;

        foreach (var (address, balance) in balances)
            _balances[address] = balance;
        foreach (var (address, nonce) in nonces)
            _nonces[address] = nonce;
        _transactionIds.UnionWith(ids);

        error = null;
        return true;

        long BalanceOf(string address) => balances.TryGetValue(address, out var pending) ? pending : GetBalance(address);
    }
}
