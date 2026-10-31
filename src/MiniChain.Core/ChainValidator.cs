using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>Detects tampering by re-checking every block in a chain.</summary>
public static class ChainValidator
{
    /// <summary>
    /// Validates a whole chain: it must start with the expected genesis block, every later block must
    /// pass <see cref="ValidateBlock"/> against the block before it, and replaying all transactions
    /// from scratch must follow the transaction rules.
    /// </summary>
    public static bool ValidateChain(IReadOnlyList<Block> blocks, ChainParameters parameters, [NotNullWhen(false)] out string? error) =>
        TryValidate(blocks, parameters, out _, out error);

    /// <summary>
    /// Validates a whole chain like <see cref="ValidateChain"/> and, if it is valid, returns it together
    /// with the ledger state it produces, ready to be adopted by <see cref="Blockchain.TryReplaceChain(ValidatedChain, out IReadOnlyList{Transaction}, out string?)"/>.
    /// </summary>
    public static bool TryValidate(IReadOnlyList<Block> blocks, ChainParameters parameters,
        [NotNullWhen(true)] out ValidatedChain? chain, [NotNullWhen(false)] out string? error)
    {
        chain = null;
        if (blocks.Count == 0)
        {
            error = "The chain is empty.";
            return false;
        }

        // Comparing both the stored and the recomputed hash checks every header field at once. The
        // genesis block has no transactions, which is checked first so a malformed block cannot crash the hashing.
        if (blocks[0] is not { Transactions.Count: 0 } genesis || genesis.Hash != Genesis.Hash || genesis.ComputeHash() != Genesis.Hash)
        {
            error = "Block #0 is not the expected genesis block.";
            return false;
        }

        // Copy the list so later changes to the caller's list cannot alter a chain that was already validated.
        var copy = blocks.ToArray();
        var ledger = new LedgerState();
        for (var i = 1; i < copy.Length; i++)
        {
            if (copy[i] is null)
            {
                error = $"Block #{i}: the block is missing.";
                return false;
            }

            var expectedDifficulty = DifficultyAdjustment.ExpectedDifficulty(copy, i, parameters);
            if (!ValidateBlock(copy[i], copy[i - 1], parameters, expectedDifficulty, out var blockError) ||
                !ledger.TryApplyBlock(copy[i], parameters, out blockError))
            {
                error = $"Block #{i}: {blockError}";
                return false;
            }
        }

        chain = new ValidatedChain(copy, ledger, parameters);
        error = null;
        return true;
    }

    /// <summary>
    /// Checks that <paramref name="block"/>'s header is intact, correctly linked to
    /// <paramref name="previous"/> and carries a valid proof of work. Transactions are checked by
    /// <see cref="LedgerState"/>.
    /// </summary>
    public static bool ValidateBlock(Block block, Block previous, ChainParameters parameters, int expectedDifficulty,
        [NotNullWhen(false)] out string? error)
    {
        if (!ValidateShape(block, parameters, out error))
            return false;

        var computedHash = block.ComputeHash();
        if (block.Hash != computedHash)
            error = $"the stored hash {block.Hash} does not match the block's contents (computed {computedHash}).";
        else if (block.PreviousHash != previous.Hash)
            error = $"the previous hash {block.PreviousHash} does not match the hash of block #{previous.Index} ({previous.Hash}).";
        else if (block.Index != previous.Index + 1)
            error = $"the index is {block.Index}, expected {previous.Index + 1}.";
        else if (block.Timestamp < previous.Timestamp)
            error = $"the timestamp {block.Timestamp} is earlier than the previous block's timestamp {previous.Timestamp}.";
        else if (block.Difficulty != expectedDifficulty)
            error = $"the difficulty is {block.Difficulty}, but the network expects {expectedDifficulty}.";
        else if (!ProofOfWork.MeetsDifficulty(block.Hash, block.Difficulty))
            error = $"the hash does not start with {block.Difficulty} zeros, so the block was never mined.";
        else
            error = null;

        return error is null;
    }

    /// <summary>
    /// The checks that need nothing but the block itself: its fields are present, its hash matches its
    /// contents and meets the difficulty it claims, and that difficulty is possible on this network. A node
    /// runs this before believing that a block it cannot connect yet means a peer is ahead, so a forged
    /// block costs real work instead of triggering a chain download for free.
    /// </summary>
    public static bool ValidateProofOfWork(Block block, ChainParameters parameters, [NotNullWhen(false)] out string? error)
    {
        if (!ValidateShape(block, parameters, out error))
            return false;

        var possibleDifficulty = parameters.AdjustDifficulty ? block.Difficulty >= 1 : block.Difficulty == parameters.Difficulty;
        var computedHash = block.ComputeHash();
        if (block.Hash != computedHash)
            error = $"the stored hash {block.Hash} does not match the block's contents (computed {computedHash}).";
        else if (!possibleDifficulty)
            error = $"the difficulty {block.Difficulty} is not possible on this network.";
        else if (!ProofOfWork.MeetsDifficulty(block.Hash, block.Difficulty))
            error = $"the hash does not start with {block.Difficulty} zeros, so the block was never mined.";
        else
            error = null;

        return error is null;
    }

    /// <summary>
    /// Rejects blocks dated more than <see cref="ChainParameters.MaxFutureBlockTimeMs"/> ahead of the local
    /// clock. A node applies this to every block it receives. Without it, one block dated years ahead would
    /// drag every later timestamp along (a block may not be older than its parent), and with difficulty
    /// adjustment on, the difficulty would only ever go up.
    /// </summary>
    public static bool ValidateNotInFuture(Block block, long nowMs, ChainParameters parameters, [NotNullWhen(false)] out string? error)
    {
        error = block.Timestamp - nowMs > parameters.MaxFutureBlockTimeMs
            ? $"the timestamp {block.Timestamp} is more than {parameters.MaxFutureBlockTimeMs / 1000} seconds in the future."
            : null;
        return error is null;
    }

    // Cheap checks that come before any hashing: blocks can arrive from the network, so required fields
    // may be missing, and a block with far too many transactions should not cost a hash per transaction.
    private static bool ValidateShape(Block block, ChainParameters parameters, [NotNullWhen(false)] out string? error)
    {
        if (block.PreviousHash is null || block.Hash is null || block.Transactions is null || block.Transactions.Any(t => t is null))
            error = "the block is malformed: a required field is missing.";
        else if (block.Transactions.Count - 1 > parameters.MaxTransactionsPerBlock)
            error = $"the block has {block.Transactions.Count - 1} transactions besides the coinbase; the limit is {parameters.MaxTransactionsPerBlock}.";
        else
            error = null;

        return error is null;
    }
}
