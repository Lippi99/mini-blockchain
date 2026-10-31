using System.Diagnostics;
using System.Globalization;
using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>
/// Times the naive, optimized and parallel miners at increasing difficulty. All three find the
/// same nonce for the same block, so they do identical work and the times compare directly.
/// The output is a Markdown table that can be pasted into the README.
/// </summary>
internal static class Benchmark
{
    public static int Run(CommandLine options)
    {
        var maxDifficulty = options.GetInt("max-difficulty", 6);
        var samples = options.GetInt("samples", 3);
        if (maxDifficulty is < 1 or > 8 || samples < 1)
            throw new CliUsageException("--max-difficulty must be between 1 and 8 and --samples at least 1.");

        // Warm up so JIT compilation does not count against the first row.
        Miner.Mine(Candidate(1, -1));
        Miner.MineSequential(Candidate(1, -1));
        Miner.MineParallel(Candidate(1, -1));

        Console.WriteLine($"Mining benchmark: {samples} block(s) per difficulty, parallel miner with {Environment.ProcessorCount} workers.");
        Console.WriteLine("Times are per block. Hash rate = hashes needed to find the nonce / time.");
        Console.WriteLine();
        Console.WriteLine("| Difficulty | Avg. hashes | Naive | Optimized | Parallel | Parallel vs. naive |");
        Console.WriteLine("|---:|---:|---:|---:|---:|---:|");

        for (var difficulty = 1; difficulty <= maxDifficulty; difficulty++)
        {
            long hashes = 0;
            TimeSpan naive = default, optimized = default, parallel = default;
            for (var sample = 0; sample < samples; sample++)
            {
                naive += Time(() => Miner.Mine(Candidate(difficulty, sample)), out var nonce);
                optimized += Time(() => Miner.MineSequential(Candidate(difficulty, sample)), out _);
                parallel += Time(() => Miner.MineParallel(Candidate(difficulty, sample)), out _);
                hashes += nonce + 1;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {difficulty} | {hashes / samples:N0} | {Cell(naive, hashes, samples)} | {Cell(optimized, hashes, samples)} | {Cell(parallel, hashes, samples)} | {naive / parallel:F1}x |"));
        }
        return 0;
    }

    private static Block Candidate(int difficulty, int sample) => new()
    {
        Index = 1,
        Timestamp = Genesis.Timestamp,
        PreviousHash = Genesis.Hash,
        Difficulty = difficulty,
        Transactions = [$"benchmark block {sample}"],
    };

    private static TimeSpan Time(Func<Block> mine, out long nonce)
    {
        var stopwatch = Stopwatch.StartNew();
        var block = mine();
        stopwatch.Stop();
        nonce = block.Nonce;
        return stopwatch.Elapsed;
    }

    private static string Cell(TimeSpan total, long hashes, int samples)
    {
        var perBlock = total.TotalSeconds / samples;
        var rate = hashes / total.TotalSeconds;
        var time = perBlock >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{perBlock:F2} s")
            : string.Create(CultureInfo.InvariantCulture, $"{perBlock * 1000:F2} ms");
        var hashRate = rate >= 1e6
            ? string.Create(CultureInfo.InvariantCulture, $"{rate / 1e6:F2} MH/s")
            : string.Create(CultureInfo.InvariantCulture, $"{rate / 1e3:F0} kH/s");
        return $"{time} ({hashRate})";
    }
}
