using Microsoft.Extensions.Options;

namespace MiniChain.Node;

/// <summary>
/// When <see cref="NodeOptions.AutoMine"/> is on, keeps mining while transactions are pending. An attempt
/// is cancelled as soon as a block from a peer changes the tip (see <see cref="MiningService"/>).
/// </summary>
public sealed class BackgroundMiner(NodeState node, MiningService mining, IOptions<NodeOptions> options, ILogger<BackgroundMiner> logger)
    : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.AutoMine)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (node.PendingTransactionCount == 0)
                    await Task.Delay(IdleDelay, stoppingToken);
                else
                    await mining.MineBlockAsync(minerAddress: null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // Keep the node alive: an unhandled exception in a background service stops the whole host.
                Log.BackgroundMiningFailed(logger, e);
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
