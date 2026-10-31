# MiniChain

An educational blockchain built from scratch in C# on .NET 10: blocks linked by SHA-256 hashes,
proof-of-work mining, transactions with ECDSA signatures, and a network of HTTP nodes that agree
on a single chain.

> ⚠️ **Educational project.** MiniChain is not secure enough for real money. Do not use it to store
> or move anything of value.

The step-by-step plan this repository follows lives in [docs/MiniChain-Action-Plan.md](docs/MiniChain-Action-Plan.md).

## Repository layout

```
MiniBlockChain/
├── src/
│   ├── MiniChain.Core/        # Class library: blocks, chain, mining, transactions, crypto
│   ├── MiniChain.Cli/         # Console app: local experiments and demos
│   └── MiniChain.Node/        # ASP.NET Core app: a network node with an HTTP API
├── tests/
│   ├── MiniChain.Core.Tests/  # Unit tests
│   └── MiniChain.Node.Tests/  # Integration tests
├── Directory.Build.props      # Settings shared by every project
└── MiniBlockChain.slnx
```

## Build and test

```bash
dotnet build
dotnet test
```

## Mining benchmark

Run it yourself with `dotnet run --project src/MiniChain.Cli -c Release -- bench`.

Measured on an Intel Core i7-11800H laptop (8 cores, 16 threads), Windows 11, .NET 10, Release
build. Each row averages 3 blocks. Times are per block, and the hash rate is *hashes needed ÷ time*.
All three miners find the same nonce for the same block, so they do exactly the same work.

| Difficulty | Avg. hashes | Naive | Optimized | Parallel | Parallel vs. naive |
|---:|---:|---:|---:|---:|---:|
| 1 | 11 | 0.20 ms (58 kH/s) | 0.10 ms (112 kH/s) | 4.49 ms (3 kH/s) | 0.0x |
| 2 | 174 | 1.49 ms (117 kH/s) | 0.16 ms (1.06 MH/s) | 4.40 ms (40 kH/s) | 0.3x |
| 3 | 2,664 | 18.37 ms (145 kH/s) | 1.43 ms (1.86 MH/s) | 3.75 ms (711 kH/s) | 4.9x |
| 4 | 36,850 | 189.55 ms (194 kH/s) | 22.65 ms (1.63 MH/s) | 15.47 ms (2.38 MH/s) | 12.3x |
| 5 | 416,114 | 1.08 s (384 kH/s) | 170.50 ms (2.44 MH/s) | 27.95 ms (14.89 MH/s) | 38.8x |
| 6 | 9,350,950 | 20.62 s (453 kH/s) | 4.01 s (2.33 MH/s) | 922.57 ms (10.14 MH/s) | 22.4x |

- Each extra zero multiplies the expected work by 16.
- **Naive** (`Miner.Mine`) rebuilds and hashes the whole header string on every attempt.
- **Optimized** (`Miner.MineSequential`) builds the header bytes once, writes only the nonce digits
  into a reusable buffer, hashes with a reusable `IncrementalHash` and checks the leading zero bits on
  the raw bytes. Nothing is allocated inside the loop.
- **Parallel** (`Miner.MineParallel`) runs one worker per logical processor; worker *i* tries the
  nonces *i, i + N, i + 2N, …*. Starting 16 threads costs a few milliseconds, so it only pays off from
  difficulty 4. At difficulties 5–6 it is 4–6x faster than the optimized miner: the 16 threads share
  8 physical cores, and laptop clocks drop under full load.

Two lessons from building the miners:

- On Windows, the one-shot `SHA256.HashData` scales poorly across threads (4.4 vs. 12.2 million
  hashes per second on 16 threads in a micro-benchmark). A reusable `IncrementalHash` per worker fixes it.
- `Parallel.For` runs long, CPU-bound loops on thread-pool threads. With all of them busy, timers and
  HTTP requests starve: a 100 ms cancellation timeout took 17 seconds to fire. The parallel miner
  uses dedicated threads instead.
