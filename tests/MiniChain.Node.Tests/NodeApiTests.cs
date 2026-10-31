using System.Net;
using System.Net.Http.Json;
using System.Text;
using MiniChain.Core;

namespace MiniChain.Node.Tests;

/// <summary>The HTTP API of a single node: status codes and JSON shapes.</summary>
public sealed class NodeApiTests : IAsyncLifetime
{
    private readonly TestNetwork _network = new();
    private readonly Wallet _alice = new();
    private readonly Wallet _bob = new();
    private readonly Wallet _mallory = new();
    private TestNetwork.TestNode _node = null!;

    public Task InitializeAsync()
    {
        _node = _network.StartNode("node1");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _network.DisposeAsync();
        _alice.Dispose();
        _bob.Dispose();
        _mallory.Dispose();
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static async Task<string> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorResponse>(MiniChainJson.Options))!.Error;

    [Fact]
    public async Task A_new_node_serves_only_the_genesis_block()
    {
        var chain = await _node.GetChainAsync();
        var status = await _node.GetStatusAsync();

        Assert.Equal(1, chain.Length);
        Assert.Equal(Genesis.Hash, Assert.Single(chain.Blocks).Hash);
        Assert.Equal("1", chain.CumulativeWork);
        Assert.Equal("http://node1", status.Url);
        Assert.Equal(0, status.Height);
        Assert.Equal(1, status.NextDifficulty);
        Assert.True(Address.IsValid(status.MinerAddress));
    }

    [Fact]
    public async Task Mining_rewards_the_requested_address()
    {
        var block = await _node.MineAsync(_alice.Address);

        Assert.Equal(1, block.Index);
        Assert.StartsWith("0", block.Hash);
        Assert.Equal(_alice.Address, block.Transactions[0].To);
        Assert.Equal(block.Hash, (await _node.Client.GetFromJsonAsync<Block>("/chain/latest", MiniChainJson.Options))!.Hash);

        var account = await _node.GetAccountAsync(_alice.Address);
        Assert.Equal(new AccountResponse(_alice.Address, 50, 0, 1), account);
    }

    [Fact]
    public async Task Mining_without_an_address_pays_the_node_itself()
    {
        var minerAddress = (await _node.GetStatusAsync()).MinerAddress;

        await _node.MineAsync();

        Assert.Equal(50, (await _node.GetAccountAsync(minerAddress)).Balance);
    }

    [Fact]
    public async Task Mining_to_an_invalid_address_is_rejected()
    {
        using var response = await _node.Client.PostAsync("/mine?minerAddress=nobody", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_transaction_is_accepted_once_and_then_mined()
    {
        await _node.MineAsync(_alice.Address);
        var payment = _alice.CreateTransaction(_bob.Address, amount: 10, fee: 1, nonce: 1, Now());

        using var first = await _node.SubmitTransactionAsync(payment);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(payment.Id, (await first.Content.ReadFromJsonAsync<TransactionAcceptedResponse>(MiniChainJson.Options))!.Id);

        using var second = await _node.SubmitTransactionAsync(payment);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        Assert.Equal([payment.Id], (await _node.GetMempoolAsync()).Select(t => t.Id));
        Assert.Equal(2, (await _node.GetAccountAsync(_alice.Address)).NextNonce);

        var block = await _node.MineAsync(_bob.Address);

        Assert.Equal(payment.Id, block.Transactions[1].Id);
        Assert.Empty(await _node.GetMempoolAsync());
        Assert.Equal(39, (await _node.GetAccountAsync(_alice.Address)).Balance);
        Assert.Equal(10 + 50 + 1, (await _node.GetAccountAsync(_bob.Address)).Balance);
    }

    [Fact]
    public async Task Invalid_transactions_are_rejected_with_a_reason()
    {
        await _node.MineAsync(_alice.Address);

        using var overspend = await _node.SubmitTransactionAsync(_alice.CreateTransaction(_bob.Address, 500, 0, 1, Now()));
        Assert.Equal(HttpStatusCode.BadRequest, overspend.StatusCode);
        Assert.Contains("only has 50 available", await ErrorOf(overspend));

        var forged = _mallory.Sign(new Transaction
        {
            From = _alice.Address,
            To = _mallory.Address,
            Amount = 5,
            Nonce = 1,
            Timestamp = Now(),
            PublicKey = _mallory.PublicKeyHex,
        });
        using var forgedResponse = await _node.SubmitTransactionAsync(forged);
        Assert.Equal(HttpStatusCode.BadRequest, forgedResponse.StatusCode);
        Assert.Contains("does not belong to the sender's address", await ErrorOf(forgedResponse));

        Assert.Empty(await _node.GetMempoolAsync());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "from": "a", "to": "b" }""")]
    [InlineData("""{ "from": null, "to": "b", "amount": 1 }""")]
    public async Task Malformed_transaction_json_is_rejected(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _node.Client.PostAsync("/transactions", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_block_with_a_wrong_hash_is_rejected()
    {
        var block = await NextBlockAsync();
        Miner.MineSequential(block);
        block.Hash = new string('0', 64);

        using var response = await _node.SubmitBlockAsync(block);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not match the block's contents", await ErrorOf(response));
        Assert.Equal(0, (await _node.GetStatusAsync()).Height);
    }

    [Fact]
    public async Task A_mined_block_with_a_forged_signature_is_rejected()
    {
        await _node.MineAsync(_alice.Address);
        var forged = _mallory.Sign(new Transaction
        {
            From = _alice.Address,
            To = _mallory.Address,
            Amount = 50,
            Nonce = 1,
            Timestamp = Now(),
            PublicKey = _alice.PublicKeyHex,
        });
        var block = await NextBlockAsync(forged);
        Miner.MineSequential(block);

        using var response = await _node.SubmitBlockAsync(block);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("the signature is not valid", await ErrorOf(response));
        Assert.Equal(50, (await _node.GetAccountAsync(_alice.Address)).Balance);
    }

    [Fact]
    public async Task A_block_that_claims_to_be_ahead_without_proof_of_work_is_rejected()
    {
        var block = await NextBlockAsync();
        block.Index += 5;
        block.PreviousHash = new string('a', 64);
        block.Hash = block.ComputeHash();
        if (ProofOfWork.MeetsDifficulty(block.Hash, block.Difficulty))
            block.Hash = "f" + block.Hash[1..]; // Make sure it was not mined by luck.

        using var response = await _node.SubmitBlockAsync(block);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_block_from_a_peer_is_appended_once()
    {
        var block = await NextBlockAsync();
        Miner.MineSequential(block);

        using var first = await _node.SubmitBlockAsync(block);
        using var second = await _node.SubmitBlockAsync(block);

        Assert.Equal("appended", (await first.Content.ReadFromJsonAsync<BlockReceivedResponse>(MiniChainJson.Options))!.Status);
        Assert.Equal("known", (await second.Content.ReadFromJsonAsync<BlockReceivedResponse>(MiniChainJson.Options))!.Status);
        Assert.Equal(1, (await _node.GetStatusAsync()).Height);
    }

    [Fact]
    public async Task Balances_need_a_valid_address()
    {
        using var response = await _node.Client.GetAsync("/balance/not-an-address");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Peers_can_be_added_listed_and_removed()
    {
        using var added = await _node.Client.PostAsJsonAsync("/peers", new PeerRequest("http://node2/"), MiniChainJson.Options);
        Assert.Equal(new PeerResponse("http://node2", Added: true), await added.Content.ReadFromJsonAsync<PeerResponse>(MiniChainJson.Options));

        using var again = await _node.Client.PostAsJsonAsync("/peers", new PeerRequest("http://NODE2"), MiniChainJson.Options);
        Assert.Equal(new PeerResponse("http://node2", Added: false), await again.Content.ReadFromJsonAsync<PeerResponse>(MiniChainJson.Options));

        Assert.Equal(["http://node2"], await _node.GetPeersAsync());

        using var self = await _node.Client.PostAsJsonAsync("/peers", new PeerRequest("http://node1"), MiniChainJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var invalid = await _node.Client.PostAsJsonAsync("/peers", new PeerRequest("ftp://node3"), MiniChainJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var removed = await _node.Client.DeleteAsync("/peers?url=http%3A%2F%2Fnode2");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var removedAgain = await _node.Client.DeleteAsync("/peers?url=http%3A%2F%2Fnode2");
        Assert.Equal(HttpStatusCode.NotFound, removedAgain.StatusCode);
        Assert.Empty(await _node.GetPeersAsync());
    }

    /// <summary>An unmined block that would extend the node's current tip.</summary>
    private async Task<Block> NextBlockAsync(params Transaction[] transactions)
    {
        var tip = (await _node.Client.GetFromJsonAsync<Block>("/chain/latest", MiniChainJson.Options))!;
        var index = tip.Index + 1;
        var timestamp = Math.Max(Now(), tip.Timestamp);
        return new Block
        {
            Index = index,
            Timestamp = timestamp,
            PreviousHash = tip.Hash,
            Difficulty = 1,
            Transactions = [Transaction.CreateCoinbase(_bob.Address, 50 + transactions.Sum(t => t.Fee), index, timestamp), .. transactions],
        };
    }
}
