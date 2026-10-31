using System.Globalization;
using System.Text;

namespace MiniChain.Core;

/// <summary>
/// A block: a header (index, timestamp, link to the previous block, difficulty and nonce) plus a
/// payload of transactions. The block's hash is computed from the header only; the payload takes
/// part through <see cref="ComputeTransactionsHash"/>.
/// </summary>
public sealed class Block
{
    /// <summary>Position in the chain (the genesis block is 0).</summary>
    public long Index { get; set; }

    /// <summary>Creation time in Unix milliseconds.</summary>
    public long Timestamp { get; set; }

    /// <summary>Hash of the previous block.</summary>
    public string PreviousHash { get; set; } = "";

    /// <summary>The payload. Plain strings for now; real transactions arrive in Phase 6.</summary>
    public IReadOnlyList<string> Transactions { get; set; } = [];

    /// <summary>Required number of leading zero hex characters in <see cref="Hash"/>.</summary>
    public int Difficulty { get; set; }

    /// <summary>The value changed during mining.</summary>
    public long Nonce { get; set; }

    /// <summary>This block's hash, as lowercase hex.</summary>
    public string Hash { get; set; } = "";

    /// <summary>
    /// Hashes the payload in a fixed order. Each transaction is hashed on its own and the
    /// concatenation of those fixed-length hashes is hashed again, so no separator character can
    /// make two different lists produce the same input.
    /// </summary>
    public string ComputeTransactionsHash()
    {
        var builder = new StringBuilder(Transactions.Count * 64);
        foreach (var transaction in Transactions)
            builder.Append(HashUtil.Sha256Hex(transaction));
        return HashUtil.Sha256Hex(builder.ToString());
    }

    /// <summary>
    /// Hashes the canonical header <c>{Index}|{Timestamp}|{PreviousHash}|{TransactionsHash}|{Difficulty}|{Nonce}</c>.
    /// Numbers are formatted with the invariant culture so every node builds exactly the same string.
    /// </summary>
    public string ComputeHash() =>
        HashUtil.Sha256Hex(string.Create(CultureInfo.InvariantCulture,
            $"{Index}|{Timestamp}|{PreviousHash}|{ComputeTransactionsHash()}|{Difficulty}|{Nonce}"));
}
