using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace MiniChain.Node;

/// <summary>
/// Catches this node up with its peers by downloading a peer's whole chain and applying the consensus
/// rule to it. Syncs run one at a time in the background, when a block shows that a peer is ahead, when a
/// peer is added, and periodically as a safety net for gossip that never arrived.
/// </summary>
public sealed class ChainSyncService(
    NodeState node,
    PeerRegistry peers,
    IPeerClient client,
    GossipService gossip,
    IHostApplicationLifetime lifetime,
    IOptions<NodeOptions> options,
    ILogger<ChainSyncService> logger) : BackgroundService
{
    private readonly Channel<string> _requests = Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);

    /// <summary>Queues a sync with <paramref name="peer"/>. A peer that is already queued is not queued twice.</summary>
    public void RequestSync(string peer)
    {
        if (_queued.TryAdd(peer, 0))
            _requests.Writer.TryWrite(peer);
    }

    public void RequestSyncWithAllPeers()
    {
        foreach (var peer in peers.GetPeers())
            RequestSync(peer);
    }

    /// <summary>Downloads <paramref name="peer"/>'s chain and adopts it if it is valid and has more work than ours.</summary>
    public async Task SyncWithAsync(string peer, CancellationToken cancellationToken)
    {
        IReadOnlyList<Core.Block> blocks;
        try
        {
            blocks = await client.GetChainAsync(peer, cancellationToken);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            Log.PeerUnreachable(logger, peer, "sync", e.Message);
            return;
        }

        var result = node.TryAdoptChain(blocks);
        switch (result.Status)
        {
            case ChainAdoption.Replaced:
                var returned = result.Returned ?? [];
                Log.ChainReplaced(logger, peer, result.OldHeight, result.NewHeight, returned.Count);
                gossip.BroadcastBlock(node.GetLatestBlock(), except: peer); // Let the rest of the network catch up too.
                foreach (var transaction in returned)
                    gossip.BroadcastTransaction(transaction); // They were mined on the losing side; get them mined again.
                break;
            case ChainAdoption.NotBetter:
                Log.ChainKept(logger, peer, result.Error!);
                break;
            default:
                Log.ChainRejected(logger, peer, result.Error!);
                break;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Peers can only be contacted once this node's own URL is known, which is after the server has started.
        await WhenStartedAsync(stoppingToken);

        foreach (var peer in peers.GetPeers())
        {
            gossip.Introduce(peer);
            RequestSync(peer);
        }

        await Task.WhenAll(ProcessRequestsAsync(stoppingToken), PollPeersAsync(stoppingToken));
    }

    private async Task ProcessRequestsAsync(CancellationToken stoppingToken)
    {
        await foreach (var peer in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            _queued.TryRemove(peer, out _);
            await SyncWithAsync(peer, stoppingToken);
        }
    }

    private async Task PollPeersAsync(CancellationToken stoppingToken)
    {
        if (options.Value.SyncIntervalSeconds <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SyncIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var ourHeight = node.GetLatestBlock().Index;
            foreach (var peer in peers.GetPeers())
            {
                try
                {
                    var theirs = await client.GetLatestBlockAsync(peer, stoppingToken);
                    if (theirs.Index > ourHeight)
                        RequestSync(peer);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    Log.PeerUnreachable(logger, peer, "poll", e.Message);
                }
            }
        }
    }

    private async Task WhenStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
        await started.Task;
    }
}
