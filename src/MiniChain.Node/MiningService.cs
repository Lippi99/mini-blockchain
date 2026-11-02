using Microsoft.Extensions.Options;
using MiniChain.Core;

namespace MiniChain.Node;

/// <summary>Mines blocks on top of the node's chain. Used by <c>POST /mine</c> and by <see cref="BackgroundMiner"/>.</summary>
public sealed class MiningService(
    NodeState node,
    NodeIdentity identity,
    GossipService gossip,
    TimeProvider time,
    IOptions<NodeOptions> options,
    ILogger<MiningService> logger) : IDisposable
{
    private const int MaxAttempts = 3;

    // The parallel miner already uses every processor, so a second attempt at once would only compete with
    // the first for the same block.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>
    /// Mines one block with the best pending transactions, rewarding <paramref name="minerAddress"/> (or
    /// the node's own address). Callers take turns. If a block from a peer changes the tip while mining, the
    /// attempt is abandoned and restarted on the new tip. Returns null if the node kept losing that race.
    /// </summary>
    public async Task<Block?> MineBlockAsync(string? minerAddress, CancellationToken cancellationToken)
    {
        minerAddress ??= identity.MinerAddress;
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var (candidate, tipChanged) = node.CreateCandidateBlock(minerAddress, time.GetUtcNow().ToUnixTimeMilliseconds());
                using var mining = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, tipChanged);
                try
                {
                    // MineParallel blocks until it finds a nonce, so it runs off the request thread.
                    await Task.Run(() => Miner.MineParallel(candidate, options.Value.MiningThreads, mining.Token), CancellationToken.None);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Log.MiningRestarted(logger, candidate.Index);
                    continue;
                }

                var result = node.SubmitBlock(candidate);
                if (result.Status == BlockSubmission.Appended)
                {
                    Log.BlockMined(logger, candidate.Index, candidate.Hash, candidate.Transactions.Count - 1);
                    gossip.BroadcastBlock(candidate);
                    return candidate;
                }
                Log.MinedBlockNotAppended(logger, candidate.Index, result.Error ?? result.Status.ToString());
            }
            return null;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public void Dispose() => _oneAtATime.Dispose();
}
