using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MiniChain.Core;

namespace MiniChain.Node.Tests;

/// <summary>Several nodes gossiping and reaching consensus: the experiments from the plan's Phase 8.</summary>
public sealed class NetworkTests : IAsyncLifetime
{
    private readonly TestNetwork _network = new();
    private readonly Wallet _alice = new();
    private readonly Wallet _bob = new();
    private readonly Wallet _carol = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _network.DisposeAsync();
        _alice.Dispose();
        _bob.Dispose();
        _carol.Dispose();
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private async Task<(TestNetwork.TestNode, TestNetwork.TestNode, TestNetwork.TestNode)> StartThreeConnectedNodesAsync()
    {
        var nodes = (_network.StartNode("node1"), _network.StartNode("node2"), _network.StartNode("node3"));
        await _network.ConnectAllAsync();
        return nodes;
    }

    [Fact]
    public async Task A_block_mined_on_one_node_reaches_the_others()
    {
        var (node1, node2, node3) = await StartThreeConnectedNodesAsync();

        var block = await node1.MineAsync();

        await Wait.UntilInSyncAsync(node1, node2, node3);
        Assert.Equal(block.Hash, (await node3.GetStatusAsync()).LatestHash);
    }

    [Fact]
    public async Task Transactions_sent_to_different_nodes_reach_everyone_and_the_nodes_agree()
    {
        var (node1, node2, node3) = await StartThreeConnectedNodesAsync();
        await node1.MineAsync(_alice.Address);
        await node1.MineAsync(_bob.Address);
        await Wait.UntilInSyncAsync(node1, node2, node3);

        using var fromAlice = await node2.SubmitTransactionAsync(_alice.CreateTransaction(_carol.Address, 10, fee: 1, nonce: 1, Now()));
        using var fromBob = await node3.SubmitTransactionAsync(_bob.CreateTransaction(_carol.Address, 20, fee: 2, nonce: 1, Now()));
        Assert.Equal(HttpStatusCode.Accepted, fromAlice.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, fromBob.StatusCode);

        foreach (var node in new[] { node1, node2, node3 })
            await Wait.UntilAsync(async () => (await node.GetMempoolAsync()).Count == 2, $"{node.Name} has both transactions");

        await node2.MineAsync(_carol.Address);
        await Wait.UntilInSyncAsync(node1, node2, node3);

        foreach (var node in new[] { node1, node2, node3 })
        {
            Assert.Empty(await node.GetMempoolAsync());
            Assert.Equal(10 + 20 + 50 + 3, (await node.GetAccountAsync(_carol.Address)).Balance);
            Assert.Equal(50 - 11, (await node.GetAccountAsync(_alice.Address)).Balance);
        }
    }

    [Fact]
    public async Task After_a_network_split_the_chain_with_more_work_wins()
    {
        var (node1, node2, node3) = (_network.StartNode("node1"), _network.StartNode("node2"), _network.StartNode("node3"));
        foreach (var (node, other) in new[] { (node1, node2), (node1, node3), (node2, node1), (node2, node3), (node3, node1), (node3, node2) })
            node.AddPeerDirectly(other); // No introductions or syncs that could still arrive during the split.
        await node1.MineAsync(_alice.Address);
        await Wait.UntilInSyncAsync(node1, node2, node3);

        // Cut node3 off from the others.
        await node3.RemovePeerAsync(node1);
        await node3.RemovePeerAsync(node2);
        await node1.RemovePeerAsync(node3);
        await node2.RemovePeerAsync(node3);

        // During the split node3 confirms a payment that the rest of the network never sees...
        var payment = _alice.CreateTransaction(_bob.Address, 5, fee: 0, nonce: 1, Now());
        using var submitted = await node3.SubmitTransactionAsync(payment);
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        await node3.MineAsync();
        await node3.MineAsync();

        // ...while node1 and node2 build a longer chain.
        for (var i = 0; i < 4; i++)
            await node1.MineAsync();
        await Wait.UntilInSyncAsync(node1, node2);
        Assert.Equal(3, (await node3.GetStatusAsync()).Height);
        Assert.Equal(5, (await node1.GetStatusAsync()).Height);

        // Reconnecting makes node3 compare chains and switch to the one with more work.
        await node3.AddPeerAsync(node1);
        await Wait.UntilInSyncAsync(node1, node2, node3);
        Assert.Equal(5, (await node3.GetStatusAsync()).Height);

        // The payment only existed in node3's discarded blocks, so it is pending again and gossiped to node1.
        await Wait.UntilAsync(async () => (await node3.GetMempoolAsync()).Any(t => t.Id == payment.Id), "node3 has the payment pending again");
        await Wait.UntilAsync(async () => (await node1.GetMempoolAsync()).Any(t => t.Id == payment.Id), "node1 received the returned payment");
    }

    [Fact]
    public async Task A_node_that_joins_late_catches_up_and_is_known_by_its_peer()
    {
        var node1 = _network.StartNode("node1");
        for (var i = 0; i < 3; i++)
            await node1.MineAsync();

        var node4 = _network.StartNode("node4");
        await node4.AddPeerAsync(node1);

        await Wait.UntilInSyncAsync(node1, node4);
        await Wait.UntilAsync(async () => (await node1.GetPeersAsync()).Contains("http://node4"), "node1 knows node4");
    }

    [Fact]
    public async Task A_longer_but_invalid_chain_from_a_peer_is_rejected()
    {
        var node1 = _network.StartNode("node1");
        var node2 = _network.StartNode("node2");
        await node1.MineAsync();
        for (var i = 0; i < 3; i++)
            await node2.MineAsync();

        // Corrupt node2's copy of block #2 behind its back: the miner awards itself 1000 coins and re-mines.
        var block = node2.Services.GetRequiredService<Blockchain>().Blocks[2];
        var coinbase = block.Transactions[0];
        block.Transactions = [Transaction.CreateCoinbase(coinbase.To, 1000, coinbase.Nonce, coinbase.Timestamp)];
        Miner.MineSequential(block);
        var tipBefore = (await node1.GetStatusAsync()).LatestHash;

        await node1.Services.GetRequiredService<ChainSyncService>().SyncWithAsync(node2.Url, CancellationToken.None);

        Assert.Equal(tipBefore, (await node1.GetStatusAsync()).LatestHash);
    }

    [Fact]
    public async Task The_background_miner_mines_pending_transactions()
    {
        var node = _network.StartNode("node1", new Dictionary<string, string?> { ["MiniChain:AutoMine"] = "true" });
        await node.MineAsync(_alice.Address);

        using var response = await node.SubmitTransactionAsync(_alice.CreateTransaction(_bob.Address, 5, fee: 1, nonce: 1, Now()));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await Wait.UntilAsync(async () => (await node.GetAccountAsync(_bob.Address)).Balance == 5, "the payment is mined");
        Assert.Empty(await node.GetMempoolAsync());
    }
}
