using System.Globalization;
using System.Text.Json.Serialization;

namespace MiniChain.Core;

/// <summary>
/// Moves <see cref="Amount"/> coins from <see cref="From"/> to <see cref="To"/>. Amounts are whole
/// numbers in the smallest unit, so no rounding ever happens.
/// </summary>
public sealed record Transaction
{
    /// <summary>The <see cref="From"/> of a coinbase transaction, which creates new coins as a mining reward.</summary>
    public const string CoinbaseSender = "COINBASE";

    /// <summary>Sender address, or <see cref="CoinbaseSender"/> for a mining reward.</summary>
    public required string From { get; init; }

    /// <summary>Recipient address.</summary>
    public required string To { get; init; }

    /// <summary>Value moved, in the smallest unit.</summary>
    public required long Amount { get; init; }

    /// <summary>Paid by the sender to the miner who includes the transaction in a block.</summary>
    public long Fee { get; init; }

    /// <summary>
    /// The sender's transaction counter: 1 for their first transaction, then 2, 3, ... A transaction
    /// is only ever valid at one position in the sender's history, so it cannot be replayed. For a
    /// coinbase this is the block index, which keeps every coinbase unique.
    /// </summary>
    public long Nonce { get; init; }

    /// <summary>Creation time in Unix milliseconds.</summary>
    public long Timestamp { get; init; }

    /// <summary>
    /// The sender's public key (hex-encoded SubjectPublicKeyInfo), so any node can verify the
    /// signature. Empty for a coinbase.
    /// </summary>
    public string? PublicKey { get; init; }

    /// <summary>Hex-encoded ECDSA signature over the <see cref="Id"/> bytes. Empty for a coinbase.</summary>
    public string? Signature { get; init; }

    /// <summary>SHA-256 of every field above except the signature.</summary>
    public string Id { get; init; } = "";

    [JsonIgnore]
    public bool IsCoinbase => From == CoinbaseSender;

    /// <summary>Hashes the canonical form <c>{From}|{To}|{Amount}|{Fee}|{Nonce}|{Timestamp}|{PublicKey}</c>.</summary>
    public string ComputeId() =>
        HashUtil.Sha256Hex(string.Create(CultureInfo.InvariantCulture, $"{From}|{To}|{Amount}|{Fee}|{Nonce}|{Timestamp}|{PublicKey}"));

    /// <summary>
    /// Creates an unsigned transaction with its <see cref="Id"/> filled in. Only a coinbase is valid
    /// without a signature; use <see cref="Wallet.CreateTransaction"/> for transfers.
    /// </summary>
    public static Transaction Create(string from, string to, long amount, long fee, long nonce, long timestamp)
    {
        var transaction = new Transaction { From = from, To = to, Amount = amount, Fee = fee, Nonce = nonce, Timestamp = timestamp };
        return transaction with { Id = transaction.ComputeId() };
    }

    /// <summary>Creates the reward transaction that a miner puts first in block <paramref name="blockIndex"/>.</summary>
    public static Transaction CreateCoinbase(string minerAddress, long amount, long blockIndex, long timestamp) =>
        Create(CoinbaseSender, minerAddress, amount, fee: 0, nonce: blockIndex, timestamp);
}
