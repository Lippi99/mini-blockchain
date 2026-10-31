namespace MiniChain.Node;

/// <summary>The node's log messages, compiled ahead of time by the logging source generator.</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Node {Url} is ready at height {Height} (difficulty {Difficulty}); mining rewards go to {MinerAddress}")]
    public static partial void NodeStarted(ILogger logger, string? url, long height, int difficulty, string minerAddress);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Mined block #{Index} {Hash} with {TransactionCount} transactions")]
    public static partial void BlockMined(ILogger logger, long index, string hash, int transactionCount);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "Abandoned mining block #{Index}: the chain tip changed")]
    public static partial void MiningRestarted(ILogger logger, long index);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Mined block #{Index} was not appended: {Reason}")]
    public static partial void MinedBlockNotAppended(ILogger logger, long index, string reason);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "Background mining failed")]
    public static partial void BackgroundMiningFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "Accepted block #{Index} {Hash} from {Origin}")]
    public static partial void BlockAccepted(ILogger logger, long index, string hash, string origin);

    [LoggerMessage(EventId = 21, Level = LogLevel.Warning, Message = "Rejected block #{Index} from {Origin}: {Reason}")]
    public static partial void BlockRejected(ILogger logger, long index, string origin, string reason);

    [LoggerMessage(EventId = 22, Level = LogLevel.Information, Message = "Block #{Index} from {Origin} is ahead of our tip #{TipIndex}; syncing")]
    public static partial void BlockAhead(ILogger logger, long index, string origin, long tipIndex);

    [LoggerMessage(EventId = 30, Level = LogLevel.Information,
        Message = "Replaced our chain with {Peer}'s: height {OldHeight} -> {NewHeight}, {Orphaned} transactions returned to the mempool")]
    public static partial void ChainReplaced(ILogger logger, string peer, long oldHeight, long newHeight, int orphaned);

    [LoggerMessage(EventId = 31, Level = LogLevel.Debug, Message = "Kept our chain instead of {Peer}'s: {Reason}")]
    public static partial void ChainKept(ILogger logger, string peer, string reason);

    [LoggerMessage(EventId = 32, Level = LogLevel.Warning, Message = "Rejected the chain from {Peer}: {Reason}")]
    public static partial void ChainRejected(ILogger logger, string peer, string reason);

    [LoggerMessage(EventId = 40, Level = LogLevel.Information, Message = "Accepted transaction {Id} from {Origin}")]
    public static partial void TransactionAccepted(ILogger logger, string id, string origin);

    [LoggerMessage(EventId = 41, Level = LogLevel.Warning, Message = "Rejected transaction {Id} from {Origin}: {Reason}")]
    public static partial void TransactionRejected(ILogger logger, string id, string origin, string reason);

    [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Dropped {Count} pending transactions that no longer fit the chain")]
    public static partial void MempoolPruned(ILogger logger, int count);

    [LoggerMessage(EventId = 50, Level = LogLevel.Information, Message = "Added peer {Peer}")]
    public static partial void PeerAdded(ILogger logger, string peer);

    [LoggerMessage(EventId = 51, Level = LogLevel.Information, Message = "Removed peer {Peer}")]
    public static partial void PeerRemoved(ILogger logger, string peer);

    [LoggerMessage(EventId = 52, Level = LogLevel.Warning, Message = "Could not reach peer {Peer} ({Action}): {Reason}")]
    public static partial void PeerUnreachable(ILogger logger, string peer, string action, string reason);
}
