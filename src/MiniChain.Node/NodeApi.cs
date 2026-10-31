using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MiniChain.Core;

namespace MiniChain.Node;

public sealed record StatusResponse(
    string? Url,
    long Height,
    string LatestHash,
    int NextDifficulty,
    string CumulativeWork,
    int PendingTransactions,
    IReadOnlyList<string> Peers,
    string MinerAddress);

public sealed record ChainResponse(int Length, string CumulativeWork, IReadOnlyList<Block> Blocks);

public sealed record AccountResponse(string Address, long Balance, long Nonce, long NextNonce);

public sealed record TransactionAcceptedResponse(string Id);

public sealed record BlockReceivedResponse(string Status);

public sealed record PeerRequest(string Url);

public sealed record PeerResponse(string Url, bool Added);

public sealed record ErrorResponse(string Error);

/// <summary>The node's HTTP API (the table in the plan's Phase 8, plus a status page and peer removal).</summary>
public static class NodeApi
{
    public static void MapNodeApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", GetStatus);
        app.MapGet("/chain", GetChain);
        app.MapGet("/chain/latest", (NodeState node) => node.GetLatestBlock());
        app.MapGet("/balance/{address}", GetBalance);
        app.MapPost("/transactions", SubmitTransaction);
        app.MapGet("/mempool", (NodeState node) => node.GetPendingTransactions());
        app.MapPost("/mine", MineAsync);
        app.MapPost("/blocks", ReceiveBlock);
        app.MapGet("/peers", (PeerRegistry peers) => peers.GetPeers());
        app.MapPost("/peers", AddPeer);
        app.MapDelete("/peers", RemovePeer);
    }

    private static StatusResponse GetStatus(NodeState node, NodeIdentity identity, PeerRegistry peers)
    {
        var summary = node.GetSummary();
        return new StatusResponse(
            identity.Url,
            summary.Height,
            summary.LatestHash,
            summary.NextDifficulty,
            summary.CumulativeWork.ToString(CultureInfo.InvariantCulture),
            summary.PendingTransactions,
            peers.GetPeers(),
            identity.MinerAddress);
    }

    private static ChainResponse GetChain(NodeState node)
    {
        var (blocks, work) = node.GetChain();
        return new ChainResponse(blocks.Count, work.ToString(CultureInfo.InvariantCulture), blocks);
    }

    private static IResult GetBalance(string address, NodeState node)
    {
        if (!Address.IsValid(address))
            return TypedResults.BadRequest(new ErrorResponse($"'{address}' is not a valid address (40 lowercase hex characters)."));

        var account = node.GetAccount(address);
        return TypedResults.Ok(new AccountResponse(account.Address, account.Balance, account.Nonce, account.NextNonce));
    }

    private static IResult SubmitTransaction(
        Transaction transaction,
        [FromHeader(Name = HttpPeerClient.OriginHeader)] string? origin,
        NodeState node,
        GossipService gossip,
        ILogger<NodeState> logger)
    {
        var from = Origin(origin);
        var result = node.SubmitTransaction(transaction);
        switch (result.Status)
        {
            case TransactionSubmission.Added:
                Log.TransactionAccepted(logger, transaction.Id, from ?? "a client");
                gossip.BroadcastTransaction(transaction, except: from);
                return TypedResults.Accepted("/mempool", new TransactionAcceptedResponse(transaction.Id));
            case TransactionSubmission.Known:
                return TypedResults.Conflict(new ErrorResponse("The transaction is already known."));
            default:
                Log.TransactionRejected(logger, transaction.Id, from ?? "a client", result.Error!);
                return TypedResults.BadRequest(new ErrorResponse(result.Error!));
        }
    }

    private static async Task<IResult> MineAsync(string? minerAddress, MiningService mining, CancellationToken cancellationToken)
    {
        if (minerAddress is not null && !Address.IsValid(minerAddress))
            return TypedResults.BadRequest(new ErrorResponse($"'{minerAddress}' is not a valid address (40 lowercase hex characters)."));

        var block = await mining.MineBlockAsync(minerAddress, cancellationToken);
        return block is not null
            ? TypedResults.Ok(block)
            : TypedResults.Conflict(new ErrorResponse("Other blocks kept arriving while mining; try again."));
    }

    private static IResult ReceiveBlock(
        Block block,
        [FromHeader(Name = HttpPeerClient.OriginHeader)] string? origin,
        NodeState node,
        GossipService gossip,
        ChainSyncService sync,
        ILogger<NodeState> logger)
    {
        var from = Origin(origin);
        var result = node.SubmitBlock(block);
        switch (result.Status)
        {
            case BlockSubmission.Appended:
                Log.BlockAccepted(logger, block.Index, block.Hash, from ?? "a client");
                gossip.BroadcastBlock(block, except: from);
                return TypedResults.Ok(new BlockReceivedResponse("appended"));
            case BlockSubmission.Known:
                return TypedResults.Ok(new BlockReceivedResponse("known"));
            case BlockSubmission.Behind:
                Log.BlockAhead(logger, block.Index, from ?? "a client", node.GetLatestBlock().Index);
                if (from is not null)
                    sync.RequestSync(from);
                else
                    sync.RequestSyncWithAllPeers();
                return TypedResults.Accepted((string?)null, new BlockReceivedResponse("syncing"));
            case BlockSubmission.Ignored:
                return TypedResults.Ok(new BlockReceivedResponse("ignored"));
            default:
                Log.BlockRejected(logger, block.Index, from ?? "a client", result.Error!);
                return TypedResults.BadRequest(new ErrorResponse(result.Error!));
        }
    }

    private static IResult AddPeer(PeerRequest request, PeerRegistry peers, GossipService gossip, ChainSyncService sync, ILogger<PeerRegistry> logger)
    {
        switch (peers.Add(request.Url, out var url))
        {
            case PeerAddResult.Added:
                Log.PeerAdded(logger, url!);
                gossip.Introduce(url!); // Make the connection mutual...
                sync.RequestSync(url!); // ...and catch up if the new peer is ahead.
                return TypedResults.Ok(new PeerResponse(url!, Added: true));
            case PeerAddResult.AlreadyKnown:
                return TypedResults.Ok(new PeerResponse(url!, Added: false));
            case PeerAddResult.Self:
                return TypedResults.BadRequest(new ErrorResponse("A node cannot be its own peer."));
            default:
                return TypedResults.BadRequest(new ErrorResponse($"'{request.Url}' is not an absolute http(s) URL such as http://localhost:5002."));
        }
    }

    private static IResult RemovePeer(string url, PeerRegistry peers, ILogger<PeerRegistry> logger)
    {
        if (!peers.Remove(url))
            return TypedResults.NotFound(new ErrorResponse($"'{url}' is not a known peer."));

        Log.PeerRemoved(logger, url);
        return TypedResults.NoContent();
    }

    private static string? Origin(string? header) => PeerRegistry.TryNormalize(header, out var url) ? url : null;
}
