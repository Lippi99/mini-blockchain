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
