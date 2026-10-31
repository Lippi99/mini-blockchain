using System.Numerics;

namespace MiniChain.Core;

/// <summary>
/// The proof-of-work rule: a block hash must start with <c>Difficulty</c> zero hex digits. Each
/// extra digit makes a valid hash about 16 times harder to find.
/// </summary>
public static class ProofOfWork
{
    /// <summary>A SHA-256 hash has 64 hex digits, so no difficulty above this can ever be met.</summary>
    public const int MaxDifficulty = 64;

    /// <summary>
    /// The expected number of hashes needed to mine a block at <paramref name="difficulty"/>: 16^difficulty.
    /// Summed over a chain this is its cumulative work, which consensus compares.
    /// </summary>
    public static BigInteger Work(int difficulty) => BigInteger.One << (4 * difficulty);

    /// <summary>Checks the leading zeros of a hex-encoded hash.</summary>
    public static bool MeetsDifficulty(string hash, int difficulty)
    {
        if (difficulty < 0 || hash.Length < difficulty)
            return false;

        for (var i = 0; i < difficulty; i++)
        {
            if (hash[i] != '0')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Checks the leading zeros directly on the raw hash bytes, without converting to hex. One hex
    /// digit is 4 bits, so a difficulty of <c>d</c> means the first <c>4 × d</c> bits must be zero.
    /// </summary>
    public static bool MeetsDifficulty(ReadOnlySpan<byte> hash, int difficulty)
    {
        if (difficulty < 0 || difficulty > hash.Length * 2)
            return false;

        var fullBytes = difficulty / 2;
        for (var i = 0; i < fullBytes; i++)
        {
            if (hash[i] != 0)
                return false;
        }

        // An odd difficulty also needs the high half (the first hex digit) of the next byte to be zero.
        return difficulty % 2 == 0 || (hash[fullBytes] & 0xF0) == 0;
    }
}
