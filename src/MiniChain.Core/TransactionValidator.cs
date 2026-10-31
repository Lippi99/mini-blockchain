using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>
/// The rules a transaction must meet on its own. Rules that depend on the chain (balance, nonce,
/// duplicates) are checked by <see cref="LedgerState"/> and <see cref="Mempool"/>.
/// </summary>
public static class TransactionValidator
{
    /// <summary>Validates a regular transfer, including its signature.</summary>
    public static bool Validate(Transaction transaction, [NotNullWhen(false)] out string? error)
    {
        if (transaction.IsCoinbase)
            error = "only the first transaction of a block can be a coinbase.";
        else if (!Address.IsValid(transaction.From))
            error = "the sender is not a valid address.";
        else if (!Address.IsValid(transaction.To))
            error = "the recipient is not a valid address.";
        else if (transaction.Amount <= 0)
            error = "the amount must be greater than zero.";
        else if (transaction.Fee < 0)
            error = "the fee cannot be negative.";
        else if (transaction.Amount > long.MaxValue - transaction.Fee)
            error = "the amount plus the fee is too large.";
        else if (transaction.Id != transaction.ComputeId())
            error = "the id does not match the transaction's contents.";
        else
            error = null;

        // The signature is checked last because it is by far the most expensive check.
        return error is null && SignatureVerifier.Verify(transaction, out error);
    }

    /// <summary>
    /// Validates the coinbase of block <paramref name="blockIndex"/>. Whether the amount is covered by
    /// the block reward plus fees is checked once the whole block is known.
    /// </summary>
    public static bool ValidateCoinbase(Transaction transaction, long blockIndex, [NotNullWhen(false)] out string? error)
    {
        if (!transaction.IsCoinbase)
            error = "the first transaction must be the coinbase.";
        else if (!Address.IsValid(transaction.To))
            error = "the coinbase recipient is not a valid address.";
        else if (transaction.Amount <= 0)
            error = "the coinbase amount must be greater than zero.";
        else if (transaction.Fee != 0)
            error = "a coinbase pays no fee.";
        else if (transaction.Nonce != blockIndex)
            error = $"the coinbase nonce must equal the block index {blockIndex}.";
        else if (transaction.Signature is not null || transaction.PublicKey is not null)
            error = "a coinbase has no public key or signature.";
        else if (transaction.Id != transaction.ComputeId())
            error = "the coinbase id does not match its contents.";
        else
            error = null;

        return error is null;
    }
}
