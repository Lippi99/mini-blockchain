using MiniChain.Core;

namespace MiniChain.Node;

/// <summary>
/// Settings from the "MiniChain" configuration section: appsettings.json, environment variables such as
/// <c>MiniChain__Difficulty=3</c>, or command-line arguments such as <c>--MiniChain:Difficulty 3</c>.
/// </summary>
public sealed class NodeOptions
{
    public const string Section = "MiniChain";

    /// <summary>The URL other nodes use to reach this one. Defaults to the first address the server listens on.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Peers to connect to at startup.</summary>
    public List<string> Peers { get; set; } = [];

    // Chain parameters. Every node in a network must use the same values, or they reject each other's blocks.
    public int Difficulty { get; set; } = 4;

    public long BlockReward { get; set; } = 50;

    public int MaxTransactionsPerBlock { get; set; } = 100;

    public bool AdjustDifficulty { get; set; }

    /// <summary>Address that receives this node's mining rewards. Defaults to a wallet the node creates for itself.</summary>
    public string? MinerAddress { get; set; }

    /// <summary>Keep mining in the background while transactions are pending.</summary>
    public bool AutoMine { get; set; }

    /// <summary>Threads used for mining; 0 means one per processor.</summary>
    public int MiningThreads { get; set; }

    /// <summary>How often to ask peers for their latest block and catch up if they are ahead; 0 turns this off.</summary>
    public int SyncIntervalSeconds { get; set; } = 30;

    /// <summary>How long to wait for a peer before giving up on a request.</summary>
    public int PeerTimeoutSeconds { get; set; } = 5;

    public ChainParameters ToChainParameters() => new()
    {
        Difficulty = Difficulty,
        BlockReward = BlockReward,
        MaxTransactionsPerBlock = MaxTransactionsPerBlock,
        AdjustDifficulty = AdjustDifficulty,
    };
}
