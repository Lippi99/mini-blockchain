using System.Threading.Channels;
using MiniChain.Core;

namespace MiniChain.Node;

/// <summary>
/// Forwards new blocks and transactions to every peer, in the background so a request never waits on the
/// network. Loops end on their own: a node only forwards items it has not seen before.
/// </summary>
public sealed class GossipService(PeerRegistry peers, IPeerClient client, ILogger<GossipService> logger) : BackgroundService
{
    private sealed record Message(string Description, string? OnlyTo, string? Except, Func<string, CancellationToken, Task> Send);

    // Gossip is best effort: if peers are too slow to keep up, the oldest messages are dropped and the
    // periodic sync in ChainSyncService fills any gap.
    private readonly Channel<Message> _queue = Channel.CreateBounded<Message>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

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
        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var targets = message.OnlyTo is { } only
                ? [only]
                : peers.GetPeers().Where(peer => peer != message.Except).ToList();
            await Task.WhenAll(targets.Select(peer => SendAsync(message, peer, stoppingToken)));
        }
    }

    private async Task SendAsync(Message message, string peer, CancellationToken stoppingToken)
    {
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
