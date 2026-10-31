using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MiniChain.Core;

/// <summary>
/// Finds a nonce that gives a block a hash with enough leading zeros. All miners set
/// <see cref="Block.Nonce"/> and <see cref="Block.Hash"/> on the candidate and return it, and all of
/// them find the same nonce: the smallest one that works.
/// </summary>
public static class Miner
{
    // A long has at most 19 digits, plus one for a minus sign.
    private const int MaxNonceBytes = 20;

    /// <summary>The straightforward miner: rebuilds the whole header string for every attempt.</summary>
    public static Block Mine(Block candidate)
    {
        var prefix = new string('0', candidate.Difficulty);
        for (long nonce = 0; ; nonce++)
        {
            candidate.Nonce = nonce;
            candidate.Hash = candidate.ComputeHash();
            if (candidate.Hash.StartsWith(prefix, StringComparison.Ordinal))
                return candidate;
        }
    }

    /// <summary>
    /// The optimized single-threaded miner. The header bytes are built once; each attempt only
    /// writes the nonce digits into a reusable buffer, hashes into a reusable span and checks the
    /// leading zero bits without converting to hex. No allocations happen inside the loop.
    /// </summary>
    /// <remarks>
    /// Hashing goes through a reusable <see cref="IncrementalHash"/> rather than the one-shot
    /// <c>SHA256.HashData</c>. On Windows every one-shot call pays extra setup inside the OS crypto
    /// library, which makes it about 1.5x slower on one thread and stops it from scaling across
    /// threads (measured: 4.4 vs. 12.2 million hashes per second on 16 threads).
    /// </remarks>
    public static Block MineSequential(Block candidate, CancellationToken cancellationToken = default)
    {
        var prefix = Encoding.UTF8.GetBytes(candidate.ComputeHeaderPrefix());
        var buffer = new byte[prefix.Length + MaxNonceBytes];
        prefix.CopyTo(buffer, 0);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];

        for (long nonce = 0; ; nonce++)
        {
            if ((nonce & 0xFFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            if (TryNonce(sha256, buffer, prefix.Length, nonce, hash, candidate.Difficulty))
                return Finish(candidate, nonce);
        }
    }

    /// <summary>
    /// Splits the search across <paramref name="workerCount"/> threads (default: one per processor).
    /// Worker <c>i</c> tries the nonces <c>i, i + N, i + 2N, ...</c> with its own copy of the header
    /// bytes and its own hash buffer; the shared <see cref="Block"/> is never touched by a worker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Workers do not stop the instant one of them succeeds, because a slower worker might still be
    /// about to try a smaller nonce. Instead each worker stops once it has passed the best nonce
    /// found so far. They run almost in lockstep, so this costs only a few extra hashes, and the
    /// result is always the smallest valid nonce: the same block the sequential miners produce.
    /// </para>
    /// <para>
    /// Each worker runs on a dedicated thread rather than through <c>Parallel.For</c>.
    /// <c>Parallel.For</c> would park these long, CPU-bound loops on every thread-pool thread, and
    /// then nothing else that needs the pool could run: not the timer behind a cancellation
    /// timeout, and not the HTTP request that tells a node to stop mining because a new block arrived.
    /// </para>
    /// </remarks>
    public static Block MineParallel(Block candidate, int workerCount = 0, CancellationToken cancellationToken = default)
    {
        if (workerCount <= 0)
            workerCount = Environment.ProcessorCount;

        var prefix = Encoding.UTF8.GetBytes(candidate.ComputeHeaderPrefix());
        var difficulty = candidate.Difficulty;
        var best = long.MaxValue;

        void Search(int worker)
        {
            var buffer = new byte[prefix.Length + MaxNonceBytes];
            prefix.CopyTo(buffer, 0);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];

            for (long nonce = worker; nonce < Volatile.Read(ref best); nonce += workerCount)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                if (TryNonce(sha256, buffer, prefix.Length, nonce, hash, difficulty))
                {
                    KeepSmallest(ref best, nonce);
                    return;
                }
            }
        }

        var workers = new Task[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            var worker = i;
            workers[i] = Task.Factory.StartNew(
                () => Search(worker), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        Task.WaitAll(workers);

        cancellationToken.ThrowIfCancellationRequested();
        return Finish(candidate, best);
    }

    private static bool TryNonce(IncrementalHash sha256, byte[] buffer, int prefixLength, long nonce, Span<byte> hash, int difficulty)
    {
        nonce.TryFormat(buffer.AsSpan(prefixLength), out var written, default, CultureInfo.InvariantCulture);
        sha256.AppendData(buffer.AsSpan(0, prefixLength + written));
        sha256.GetHashAndReset(hash);
        return ProofOfWork.MeetsDifficulty(hash, difficulty);
    }

    private static void KeepSmallest(ref long best, long nonce)
    {
        var current = Volatile.Read(ref best);
        while (nonce < current)
        {
            var seen = Interlocked.CompareExchange(ref best, nonce, current);
            if (seen == current)
                return;
            current = seen;
        }
    }

    private static Block Finish(Block candidate, long nonce)
    {
        candidate.Nonce = nonce;
        candidate.Hash = candidate.ComputeHash();
        return candidate;
    }
}
