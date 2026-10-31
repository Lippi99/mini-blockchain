# MiniChain: Action Plan for Building a Blockchain from Scratch in .NET 10

> An end-to-end plan for building an educational blockchain in C#, from installing the SDK to running a network of nodes that agree on a single chain.

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Prerequisites](#2-prerequisites)
3. [Phase 0: Environment Setup](#phase-0-environment-setup)
4. [Phase 1: Solution Structure](#phase-1-solution-structure)
5. [Phase 2: Hashing and the Block](#phase-2-hashing-and-the-block)
6. [Phase 3: The Chain and the Genesis Block](#phase-3-the-chain-and-the-genesis-block)
7. [Phase 4: Chain Validation (Tamper Detection)](#phase-4-chain-validation-tamper-detection)
8. [Phase 5: Proof of Work (Mining)](#phase-5-proof-of-work-mining)
9. [Phase 6: Transactions, Balances and Rewards](#phase-6-transactions-balances-and-rewards)
10. [Phase 7: Wallets and Digital Signatures](#phase-7-wallets-and-digital-signatures)
11. [Phase 8: Networking and Consensus](#phase-8-networking-and-consensus)
12. [Phase 9: Persistence](#phase-9-persistence)
13. [Phase 10: Testing Strategy](#phase-10-testing-strategy)
14. [Phase 11: Polish, Docker and CI](#phase-11-polish-docker-and-ci)
15. [Stretch Goals](#stretch-goals)
16. [Suggested Timeline](#suggested-timeline)
17. [Glossary](#glossary)

---

## 1. Project Overview

### Goal

Build **MiniChain**, a simplified cryptocurrency that implements the core ideas behind Bitcoin:

- Blocks linked together by cryptographic hashes
- Tamper detection
- Proof-of-work mining with adjustable difficulty
- Transactions, balances and mining rewards
- Wallets with public/private keys and digital signatures
- Multiple nodes communicating over HTTP and reaching consensus

### What this project is NOT

- It is **not** production-grade or secure enough for real money.
- It does **not** aim to be compatible with Bitcoin or any other real network.

### Key design decisions

| Decision | Choice | Reason |
|---|---|---|
| Runtime | .NET 10 (LTS) | Current long-term support release; strong built-in crypto |
| Language | C# | Strong typing, good performance, rich standard library |
| Hash function | SHA-256 | Same as Bitcoin; built into .NET |
| Signature algorithm | ECDSA on **NIST P-256** | Built into .NET on every OS (secp256k1, used by Bitcoin, is not supported on all platforms) |
| Balance model | **Account-based** (like Ethereum) | Simpler than Bitcoin's UTXO model; UTXO is listed as a stretch goal |
| Amounts | `long` in the smallest unit | Avoids floating-point rounding errors |
| Timestamps | Unix time in milliseconds (`long`) | Deterministic, with no time-zone or formatting issues |
| Networking | ASP.NET Core Minimal APIs + `HttpClient` | Simple, fast and built in |
| Tests | xUnit | Standard .NET testing framework |

---

## 2. Prerequisites

### Knowledge

- C# basics: classes, records, collections, LINQ, `async`/`await`
- Basic command-line usage
- Basic understanding of HTTP and JSON

### Tools

- **.NET 10 SDK**: https://dotnet.microsoft.com/download
- **IDE**: Visual Studio 2022+ (or the latest version), VS Code with the C# Dev Kit, or JetBrains Rider
- **Git** and a GitHub account
- **Optional:** Docker Desktop (Phase 11), and Postman, Bruno or curl for testing the API

---

## Phase 0: Environment Setup

### Tasks

1. Install the .NET 10 SDK.
2. Verify the installation:
   ```bash
   dotnet --version        # should print 10.x.x
   dotnet --list-sdks
   ```
3. Install your IDE and the C# extension, if you use VS Code.
4. Create a GitHub repository named `minichain`.
5. Clone it locally and add a .NET `.gitignore`:
   ```bash
   git clone https://github.com/<your-user>/minichain.git
   cd minichain
   dotnet new gitignore
   ```

### Done when

- [x] `dotnet --version` shows a 10.x SDK
- [x] The repository is cloned and has a `.gitignore`

---

## Phase 1: Solution Structure

### Target layout

```
minichain/
├── src/
│   ├── MiniChain.Core/        # Class library: blocks, chain, mining, transactions, crypto
│   ├── MiniChain.Cli/         # Console app: local experiments and demos
│   └── MiniChain.Node/        # ASP.NET Core app: a network node with an HTTP API
├── tests/
│   ├── MiniChain.Core.Tests/  # Unit tests
│   └── MiniChain.Node.Tests/  # Integration tests
├── MiniChain.slnx             # Solution file (.NET 10 uses the .slnx format by default)
└── README.md
```

The **Core** library holds all the blockchain logic and does not know anything about the console or HTTP. The CLI and the Node are thin layers on top of it. This keeps the logic easy to test.

### Commands

```bash
dotnet new sln -n MiniChain

dotnet new classlib -n MiniChain.Core -o src/MiniChain.Core
dotnet new console  -n MiniChain.Cli  -o src/MiniChain.Cli
dotnet new web      -n MiniChain.Node -o src/MiniChain.Node

dotnet new xunit -n MiniChain.Core.Tests -o tests/MiniChain.Core.Tests
dotnet new xunit -n MiniChain.Node.Tests -o tests/MiniChain.Node.Tests

dotnet sln add src/MiniChain.Core src/MiniChain.Cli src/MiniChain.Node
dotnet sln add tests/MiniChain.Core.Tests tests/MiniChain.Node.Tests

dotnet add src/MiniChain.Cli  reference src/MiniChain.Core
dotnet add src/MiniChain.Node reference src/MiniChain.Core
dotnet add tests/MiniChain.Core.Tests reference src/MiniChain.Core
dotnet add tests/MiniChain.Node.Tests reference src/MiniChain.Node
dotnet add tests/MiniChain.Node.Tests package Microsoft.AspNetCore.Mvc.Testing

dotnet build
dotnet test
```

### Recommended project settings

Add these to each `.csproj`, or to a shared `Directory.Build.props` file at the repository root:

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

### Done when

- [x] `dotnet build` succeeds
- [x] `dotnet test` runs (the template tests pass)
- [ ] First commit pushed: `chore: initial solution structure`

---

## Phase 2: Hashing and the Block

### Concepts

- A **hash** is a fixed-size "fingerprint" of data. Changing a single byte of the input changes the hash completely.
- A **block** is a container of data plus a header. The block's hash is computed from its header.

### Tasks

1. Create a `HashUtil` static class:
   ```csharp
   using System.Security.Cryptography;
   using System.Text;

   public static class HashUtil
   {
       public static string Sha256Hex(string input) =>
           Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
   }
   ```
2. Create the `Block` class with these fields:

   | Field | Type | Description |
   |---|---|---|
   | `Index` | `long` | Position in the chain (genesis = 0) |
   | `Timestamp` | `long` | Unix time in milliseconds |
   | `PreviousHash` | `string` | Hash of the previous block |
   | `Transactions` | `IReadOnlyList<Transaction>` | Payload (a list of strings is fine until Phase 6) |
   | `Difficulty` | `int` | Required number of leading zero hex characters |
   | `Nonce` | `long` | Value changed during mining |
   | `Hash` | `string` | This block's hash |

3. Implement `ComputeHash()` using a **deterministic, canonical header**. Every node must produce exactly the same string for the same block:
   ```
   {Index}|{Timestamp}|{PreviousHash}|{TransactionsHash}|{Difficulty}|{Nonce}
   ```
   - `TransactionsHash` is the SHA-256 of the transactions serialized in a fixed order. In a later phase you can replace it with a Merkle root (see Stretch Goals).
   - Never hash the output of `ToString()` on a `DateTime` or a `double`, because the result depends on the culture and the format.

### Pitfalls

- Serializing JSON for hashing can be non-deterministic if property order or formatting changes. Prefer an explicit, hand-built header string, or a fixed `JsonSerializerOptions` instance that you never change.
- Watch out for culture-dependent formatting. Use `CultureInfo.InvariantCulture` when converting numbers to strings.

### Done when

- [x] Unit test: the same block always produces the same hash
- [x] Unit test: changing any single field changes the hash

---

## Phase 3: The Chain and the Genesis Block

### Concepts

Each block stores the hash of the block before it. This "link" is what turns a list of blocks into a **chain**.

### Tasks

1. Create a `Blockchain` class that holds a `List<Block>`.
2. Create a **hard-coded genesis block** (index 0, `PreviousHash = "0"`, a fixed timestamp). All nodes must share the *exact same* genesis block, or they will never agree on a chain.
3. Implement:
   - `Block LatestBlock { get; }`
   - `void AddBlock(Block block)`, which sets `PreviousHash` to the latest block's hash and then computes the new block's hash. In Phase 5, computing the hash is replaced by mining.
4. In `MiniChain.Cli`, create a few blocks and print the chain to the console.

### Done when

- [ ] The CLI prints a chain of at least 3 blocks
- [ ] Every block's `PreviousHash` equals the previous block's `Hash`

---

## Phase 4: Chain Validation (Tamper Detection)

### Tasks

Implement `bool IsValid(out string? error)` on `Blockchain`. For every block after the genesis block, check that:

1. `block.Hash == block.ComputeHash()`, meaning the contents were not modified
2. `block.PreviousHash == previous.Hash`, meaning the link is intact
3. `block.Index == previous.Index + 1`
4. `block.Timestamp >= previous.Timestamp`

Also check that the first block is exactly the expected genesis block.

### Experiment

In the CLI:
1. Build a valid chain and confirm that `IsValid` returns `true`.
2. Change a transaction inside block 1.
3. Confirm that `IsValid` returns `false` and reports which block is broken.
4. Recompute block 1's hash and observe that block 2 is now broken instead.

### Done when

- [ ] Unit tests cover each of the four rules
- [ ] The tampering experiment behaves as described

---

## Phase 5: Proof of Work (Mining)

### Concepts

A block is only accepted if its hash starts with `Difficulty` zeros. The only way to find such a hash is to keep changing the `Nonce` and trying again. Each additional hex zero makes mining about **16 times harder**.

### Tasks

1. **Single-threaded miner:**
   ```csharp
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
   ```
2. Add proof-of-work checks to validation:
   - The hash starts with the required number of zeros.
   - The block's difficulty matches the network's expected difficulty.
3. **Benchmark:** measure hashes per second and the time to mine at difficulties 1 through 6. Use `System.Diagnostics.Stopwatch`.
4. **Optimize the hot loop.** Building a new string on every attempt is slow. Instead:
   - Build the header bytes once, without the nonce.
   - Append only the nonce bytes on each attempt.
   - Use `SHA256.HashData(ReadOnlySpan<byte>, Span<byte>)` with a reusable buffer to avoid allocations.
   - Check the leading zeros directly on the byte array instead of converting to hex. One hex zero equals 4 zero bits, so a difficulty of `d` means the first `4 × d` bits must be zero.
5. **Parallel miner:**
   - Use `Parallel.For(0, Environment.ProcessorCount, ...)`.
   - Worker `i` tries the nonces `i, i + N, i + 2N, ...`, where `N` is the number of workers.
   - Each worker must use **its own copy** of the header bytes and its own hash buffer. Never let workers share or modify the same `Block` object.
   - Stop all workers with a `CancellationTokenSource` as soon as one of them finds a valid nonce.
   - Compare the speed-up with the single-threaded version.
6. **Optional: difficulty adjustment.** Every 10 blocks, compare the actual mining time to a target, such as 10 seconds per block, and raise or lower the difficulty by one.

### Done when

- [ ] Mined blocks pass validation; blocks that were not mined fail it
- [ ] A benchmark table (difficulty, time, hash rate) is recorded in the README
- [ ] The parallel miner is measurably faster on a multi-core machine

---

## Phase 6: Transactions, Balances and Rewards

### Concepts

- A **transaction** moves coins from one address to another.
- The **mempool** holds pending transactions that are waiting to be mined.
- The miner adds a special **coinbase transaction** that pays itself a reward. This is how new coins are created.

### Tasks

1. Create a `Transaction` record:

   | Field | Type | Description |
   |---|---|---|
   | `From` | `string` | Sender address (`"COINBASE"` for rewards) |
   | `To` | `string` | Recipient address |
   | `Amount` | `long` | Value in the smallest unit |
   | `Fee` | `long` | Optional fee paid to the miner |
   | `Nonce` | `long` | The sender's transaction counter, which prevents replays |
   | `Timestamp` | `long` | Unix ms |
   | `Signature` | `string?` | Filled in during Phase 7 |
   | `Id` | `string` | SHA-256 of all the fields above except `Signature` |

2. Create a `Mempool` that stores pending transactions and rejects duplicates by `Id`.
3. Compute **balances** by replaying the whole chain:
   - Add the amounts received.
   - Subtract the amounts sent plus the fees.
   - Cache the results for performance later on.
4. **Validation rules** for a transaction:
   - `Amount > 0` and `Fee >= 0`
   - The sender has enough balance, counting other pending transactions in the same block
   - The `Nonce` equals the sender's next expected nonce: the last confirmed nonce + 1, plus the number of that sender's transactions already pending in the mempool (or earlier in the same block)
   - No duplicate `Id` exists in the chain or in the mempool
5. **Validation rules** for a block:
   - It contains exactly **one** coinbase transaction, and it comes first.
   - The coinbase amount is less than or equal to the block reward plus the sum of the fees.
   - Every other transaction is valid.
6. Update mining: take the transactions from the mempool (highest fee first), add the coinbase, mine the block, then remove the included transactions from the mempool.

### Experiments

- Try to spend more coins than you have, and confirm that it is rejected.
- Submit the same transaction twice, and confirm that the second copy is rejected.

### Done when

- [ ] Balances are correct after several mined blocks
- [ ] Unit tests cover every validation rule, including double spends and replays

---

## Phase 7: Wallets and Digital Signatures

### Concepts

- A **private key** signs transactions and must be kept secret.
- A **public key** verifies signatures and can be shared freely.
- An **address** is a short identifier derived from the public key.

### Tasks

1. Create a `Wallet` class:
   ```csharp
   using System.Security.Cryptography;

   public sealed class Wallet : IDisposable
   {
       private readonly ECDsa _key;

       public Wallet() => _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

       public byte[] PublicKey => _key.ExportSubjectPublicKeyInfo();

       public string Address =>
           Convert.ToHexStringLower(SHA256.HashData(PublicKey))[..40];

       public byte[] Sign(byte[] data) =>
           _key.SignData(data, HashAlgorithmName.SHA256);

       public void Dispose() => _key.Dispose();
   }
   ```
2. Add the sender's **public key** to the `Transaction` record, so other nodes can verify the signature.
3. **Sign** the transaction `Id` bytes with the sender's private key.
4. **Verify** each transaction:
   - Import the public key with `ImportSubjectPublicKeyInfo`.
   - Check that the address derived from the public key equals `From`.
   - Check that `VerifyData(idBytes, signature, HashAlgorithmName.SHA256)` returns `true`.
5. **Persist wallets:** export the private key with `ExportECPrivateKey()` (or `ExportEncryptedPkcs8PrivateKey` with a password) and save it to a file that is **excluded from Git**.
6. In the CLI, add commands to create a wallet, show its address and balance, and send coins.

### Experiments

- Change the amount of a signed transaction, and confirm that verification fails.
- Sign a transaction with wallet A while claiming to be wallet B, and confirm that it is rejected.

### Done when

- [ ] Unsigned or forged transactions are always rejected
- [ ] Private keys never appear in logs, API responses or Git history

---

## Phase 8: Networking and Consensus

This is the hardest phase. Several independent nodes must keep their own copy of the chain and still agree on which chain is the correct one.

### Architecture

Each node is an instance of `MiniChain.Node` running on its own port. It holds:

- its own `Blockchain`
- its own `Mempool`
- a list of known peers (the URLs of other nodes)

### HTTP API (Minimal APIs)

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/chain` | Return the full chain |
| `GET` | `/chain/latest` | Return the latest block |
| `GET` | `/balance/{address}` | Return the balance of an address |
| `POST` | `/transactions` | Submit a signed transaction |
| `GET` | `/mempool` | List the pending transactions |
| `POST` | `/mine` | Mine a block now (for manual testing) |
| `POST` | `/blocks` | Receive a newly mined block from a peer |
| `GET` | `/peers` | List the known peers |
| `POST` | `/peers` | Register a new peer |

### Tasks

1. **Dependency injection:** register `Blockchain`, `Mempool` and `PeerRegistry` as **singletons**, and register `HttpClient` with `builder.Services.AddHttpClient()`.
2. **Thread safety:** HTTP requests run concurrently. Protect the chain and the mempool with a `lock` (or a `SemaphoreSlim` for async code). This is a classic source of bugs.
3. **Gossip:**
   - When a node receives a new valid transaction, it forwards the transaction to its peers.
   - When a node mines a block, it sends the block to its peers through `POST /blocks`.
   - Ignore items you have already seen, to avoid infinite loops.
4. **Receiving a block:**
   - If `block.Index == tip.Index + 1` and `block.PreviousHash == tip.Hash`, validate the block and append it. Remove its transactions from the mempool.
   - If the block is further ahead, you are behind: fetch the peer's full chain (`GET /chain`) and resolve the conflict.
   - Otherwise, ignore the block.
5. **Consensus rule:** adopt the peer's chain only if it is **fully valid** and has **more cumulative work** than yours. Cumulative work is the sum of `16^difficulty` over all blocks. With a fixed difficulty, this is the same as the "longest chain" rule. When you switch chains, return the transactions from your discarded blocks to the mempool, unless they are already in the new chain.
6. **Background miner (optional):** a `BackgroundService` that keeps mining while there are pending transactions, and cancels the current attempt when a new block arrives from a peer.
7. **Running three nodes locally:**
   ```bash
   dotnet run --project src/MiniChain.Node --urls http://localhost:5001
   dotnet run --project src/MiniChain.Node --urls http://localhost:5002
   dotnet run --project src/MiniChain.Node --urls http://localhost:5003
   ```
   Then register the peers with each other through `POST /peers`.

### Experiments

1. Mine a block on node 5001, and confirm that 5002 and 5003 receive it.
2. **Network split:** disconnect node 5003, mine 2 blocks on it and 4 blocks on 5001, then reconnect. Node 5003 should drop its shorter chain and adopt the longer one.
3. Send an invalid block (with a wrong hash or a forged signature) directly to a node, and confirm that it is rejected.

### Done when

- [ ] Three nodes stay in sync while transactions are sent to random nodes
- [ ] The network-split experiment resolves correctly
- [ ] Integration tests start nodes with `WebApplicationFactory<Program>` and check that they sync

---

## Phase 9: Persistence

Right now, everything is lost when a node stops.

### Tasks

1. **Simple option:** save the chain to a JSON file after each new block, and load it when the node starts. Revalidate the whole chain on load.
2. **Better option:** use SQLite through `Microsoft.Data.Sqlite` or EF Core, with tables for blocks and transactions.
3. Store each node's data in a separate folder, chosen through configuration (`appsettings.json` or an environment variable).

### Done when

- [ ] A node can restart without losing its chain
- [ ] A corrupted data file is detected when the node starts

---

## Phase 10: Testing Strategy

### Unit tests (`MiniChain.Core.Tests`)

| Area | Example tests |
|---|---|
| Hashing | Deterministic output; a change in any field changes the hash |
| Validation | Tampered data, broken links, wrong index or timestamp, missing proof of work |
| Mining | The mined hash meets the difficulty; the parallel and sequential miners agree |
| Transactions | Insufficient funds, double spends, replays, invalid amounts, a second coinbase |
| Signatures | Valid signature, modified transaction, wrong key, address mismatch |
| Consensus | The longer valid chain wins; a longer **invalid** chain is rejected |

**Tip:** use difficulty 1 or 2 in tests, so the test suite stays fast.

### Integration tests (`MiniChain.Node.Tests`)

- Use `WebApplicationFactory<Program>` (from the `Microsoft.AspNetCore.Mvc.Testing` package) to start nodes in memory.
- Add `public partial class Program { }` at the end of `Program.cs` so the tests can reference it.
- Test the API contract (status codes and JSON shapes) and the synchronization between nodes.
- By default, nodes started by the factory run on an in-memory test server and cannot call each other through real URLs. For multi-node tests, either give each node a real port with the factory's Kestrel option (available since .NET 10), or abstract peer communication behind an interface and inject the test clients.

### Done when

- [ ] `dotnet test` passes with good coverage of the Core library
- [ ] Every bug you find gets a regression test

---

## Phase 11: Polish, Docker and CI

### Tasks

1. **README.md** with:
   - a project description and an architecture diagram
   - instructions to run one node and to run three nodes
   - API examples (curl)
   - the benchmark table from Phase 5
   - a clear warning that this is an educational project
2. **Logging:** use `ILogger` for mined blocks, received blocks, chain replacements and rejected items.
3. **Docker:**
   - Write a `Dockerfile` for `MiniChain.Node`. Alternatively, use `dotnet publish /t:PublishContainer`, which builds a container image without a Dockerfile.
   - Write a `docker-compose.yml` that starts 3 nodes, already configured as peers of each other.
4. **GitHub Actions:** a workflow that runs `dotnet build` and `dotnet test` on every push.
5. **Code quality:** enable the .NET analyzers and add an `.editorconfig` file.

### Done when

- [ ] `docker compose up` starts a working 3-node network
- [ ] The CI badge is green on the README

---

## Stretch Goals

Pick any of these after the core project is complete:

| Goal | What you learn |
|---|---|
| **Merkle tree** for transactions | Efficient proofs that a transaction is in a block |
| **UTXO model** (like Bitcoin) | A different and more parallel-friendly balance model |
| **Difficulty retargeting** based on block time | Feedback control in distributed systems |
| **Web dashboard** (Blazor) showing blocks live | Front end and real-time updates with SignalR |
| **Block explorer** page | Read-only API design and search |
| **secp256k1** through a library such as NBitcoin or BouncyCastle | Bitcoin-compatible keys |
| **Fork-attack simulation**, where a malicious node tries to rewrite history | Why a 51% attack works and what it costs |
| **gRPC instead of REST** between nodes | Efficient binary protocols |
| **Simple smart contracts** (a tiny stack-based script language) | Interpreters and virtual machines |

---

## Suggested Timeline

Assuming about 1–2 hours a day:

| Week | Phases | Milestone |
|---|---|---|
| 1 | 0–4 | A chain that detects tampering |
| 2 | 5 | Mining, benchmarks and a parallel miner |
| 3 | 6 | Transactions, balances and rewards |
| 4 | 7 | Wallets and signatures |
| 5–6 | 8 | A 3-node network that reaches consensus |
| 7 | 9–11 | Persistence, tests, Docker, CI and the README |

Commit at the end of every phase with a clear message (for example, `feat(mining): add parallel proof-of-work miner`).

---

## Glossary

| Term | Meaning |
|---|---|
| **Hash** | A fixed-size fingerprint of data; any change to the data changes the hash completely |
| **Block** | A group of transactions plus a header, identified by its hash |
| **Genesis block** | The first block of the chain, hard-coded and identical on every node |
| **Nonce (block)** | The number changed during mining to search for a valid hash |
| **Nonce (transaction)** | The sender's transaction counter, which prevents replays |
| **Difficulty** | How many leading zeros a valid block hash must have |
| **Proof of work** | Evidence that computational effort was spent to create a block |
| **Mempool** | The pool of valid transactions waiting to be included in a block |
| **Coinbase transaction** | The first transaction of a block, which pays the miner's reward |
| **Address** | A short identifier derived from a public key |
| **Digital signature** | Proof, created with a private key, that a transaction was authorized |
| **Consensus** | The rule nodes use to agree on a single valid chain |
| **Fork** | Two competing versions of the chain that coexist temporarily |
| **Double spend** | An attempt to spend the same coins twice |

---

> ⚠️ **Disclaimer:** MiniChain is an educational project. Do not use it to store or move anything of real value.
