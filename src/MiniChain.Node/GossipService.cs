using System.Threading.Channels;
using MiniChain.Core;

namespace MiniChain.Node;

/// <summary>
/// Forwards new blocks and transactions to every peer, in the background so a request never waits on the
/// network. Each peer has its own outbox and its own delivery loop, so a slow or dead peer only delays
/// itself. Loops end on their own: a node only forwards items it has not seen before.
/// </summary>
public sealed class GossipService(PeerRegistry peers, IPeerClient client, ILogger<GossipService> logger) : BackgroundService
{
    private sealed record Message(string Description, string? OnlyTo, string? Except, Func<string, CancellationToken, Task> Send);

    private readonly Channel<Message> _queue = CreateQueue();

    /// <summary>Sends <paramref name="block"/> to every peer except <paramref name="except"/> (usually the peer it came from).</summary>
    public void BroadcastBlock(Block block, string? except = null) =>
        _queue.Writer.TryWrite(new Message($"block #{block.Index}", null, except, (peer, ct) => client.SendBlockAsync(peer, block, ct)));

    public void BroadcastTransaction(Transaction transaction, string? except = null) =>
        _queue.Writer.TryWrite(new Message($"transaction {transaction.Id}", null, except, (peer, ct) => client.SendTransactionAsync(peer, transaction, ct)));

    /// <summary>Asks <paramref name="peer"/> to add this node to its peer list, so the connection goes both ways.</summary>
    public void Introduce(string peer) =>
        _queue.Writer.TryWrite(new Message("introduction", peer, null, (target, ct) => client.IntroduceAsync(target, ct)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var outboxes = new Dictionary<string, Channel<Message>>(StringComparer.Ordinal);
        var deliveries = new List<Task>();
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                var known = peers.GetPeers();

                // Close the outboxes of peers that were removed, so their delivery loops can end.
                foreach (var removed in outboxes.Keys.Except(known).ToList())
                {
                    outboxes[removed].Writer.TryComplete();
                    outboxes.Remove(removed);
                }
                deliveries.RemoveAll(delivery => delivery.IsCompleted);

                IEnumerable<string> targets = message.OnlyTo is { } only ? [only] : known.Where(peer => peer != message.Except);
                foreach (var peer in targets)
                {
                    if (!outboxes.TryGetValue(peer, out var outbox))
                    {
                        outbox = CreateQueue();
                        outboxes[peer] = outbox;
                        deliveries.Add(DeliverAsync(peer, outbox.Reader, stoppingToken));
                    }
                    outbox.Writer.TryWrite(message);
                }
            }
        }
        finally
        {
            await Task.WhenAll(deliveries);
        }
    }

    private async Task DeliverAsync(string peer, ChannelReader<Message> outbox, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in outbox.ReadAllAsync(stoppingToken))
            {
                // A peer removed while messages were still waiting (as in a network split) must not get them.
                if (message.OnlyTo is null && !peers.IsKnown(peer))
                    continue;

                try
                {
                    await message.Send(peer, stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    // One bad peer must never stop gossip: an exception escaping a background service stops the node.
                    Log.PeerUnreachable(logger, peer, message.Description, e.Message);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The node is shutting down.
        }
    }

    // Gossip is best effort: if a peer is too slow to keep up, its oldest messages are dropped, and the
    // periodic sync in ChainSyncService fills any gap in its blocks.
    private static Channel<Message> CreateQueue() => Channel.CreateBounded<Message>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
}
