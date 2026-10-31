using Microsoft.Data.Sqlite;
using MiniChain.Core;

namespace MiniChain.Node.Storage;

/// <summary>
/// The plan's "better option": blocks and transactions in two SQLite tables. Saving only rewrites the
/// blocks from the first one that changed, so appending a block or switching to a fork is cheap.
/// </summary>
public sealed class SqliteChainStore : IChainStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS blocks (
            idx           INTEGER PRIMARY KEY,
            hash          TEXT    NOT NULL,
            previous_hash TEXT    NOT NULL,
            timestamp     INTEGER NOT NULL,
            difficulty    INTEGER NOT NULL,
            nonce         INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS transactions (
            block_idx  INTEGER NOT NULL,
            position   INTEGER NOT NULL,
            id         TEXT    NOT NULL,
            sender     TEXT    NOT NULL,
            recipient  TEXT    NOT NULL,
            amount     INTEGER NOT NULL,
            fee        INTEGER NOT NULL,
            nonce      INTEGER NOT NULL,
            timestamp  INTEGER NOT NULL,
            public_key TEXT,
            signature  TEXT,
            PRIMARY KEY (block_idx, position)
        );
        """;

    private readonly string _path;
    private readonly string _connectionString;

    public SqliteChainStore(string path)
    {
        _path = path;
        // Without pooling the file is closed after every operation, so it can be copied or deleted freely.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
    }

    public string Location => _path;

    public IReadOnlyList<Block>? Load()
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            using var connection = Open();
            var blocks = new List<Block>();
            var transactions = new Dictionary<long, List<Transaction>>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT block_idx, id, sender, recipient, amount, fee, nonce, timestamp, public_key, signature FROM transactions ORDER BY block_idx, position";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var blockIndex = reader.GetInt64(0);
                    if (!transactions.TryGetValue(blockIndex, out var list))
                        transactions[blockIndex] = list = [];
                    list.Add(new Transaction
                    {
                        Id = reader.GetString(1),
                        From = reader.GetString(2),
                        To = reader.GetString(3),
                        Amount = reader.GetInt64(4),
                        Fee = reader.GetInt64(5),
                        Nonce = reader.GetInt64(6),
                        Timestamp = reader.GetInt64(7),
                        PublicKey = reader.IsDBNull(8) ? null : reader.GetString(8),
                        Signature = reader.IsDBNull(9) ? null : reader.GetString(9),
                    });
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT idx, hash, previous_hash, timestamp, difficulty, nonce FROM blocks ORDER BY idx";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var index = reader.GetInt64(0);
                    blocks.Add(new Block
                    {
                        Index = index,
                        Hash = reader.GetString(1),
                        PreviousHash = reader.GetString(2),
                        Timestamp = reader.GetInt64(3),
                        Difficulty = reader.GetInt32(4),
                        Nonce = reader.GetInt64(5),
                        Transactions = transactions.GetValueOrDefault(index) ?? [],
                    });
                }
            }

            return blocks.Count == 0 ? null : blocks;
        }
        catch (SqliteException e)
        {
            throw new InvalidDataException($"{_path} is not a readable chain database: {e.Message}", e);
        }
    }

    public void Save(IReadOnlyList<Block> blocks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        // Find the first block that differs from what is stored; everything before it is already saved.
        var storedHashes = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT hash FROM blocks ORDER BY idx";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                storedHashes.Add(reader.GetString(0));
        }
        var firstChanged = 0;
        while (firstChanged < Math.Min(storedHashes.Count, blocks.Count) && storedHashes[firstChanged] == blocks[firstChanged].Hash)
            firstChanged++;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM transactions WHERE block_idx >= $from; DELETE FROM blocks WHERE idx >= $from;";
            command.Parameters.AddWithValue("$from", firstChanged);
            command.ExecuteNonQuery();
        }

        foreach (var block in blocks.Skip(firstChanged))
            Insert(connection, block);

        transaction.Commit();
    }

    private static void Insert(SqliteConnection connection, Block block)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO blocks (idx, hash, previous_hash, timestamp, difficulty, nonce) VALUES ($idx, $hash, $previous, $timestamp, $difficulty, $nonce)";
            command.Parameters.AddWithValue("$idx", block.Index);
            command.Parameters.AddWithValue("$hash", block.Hash);
            command.Parameters.AddWithValue("$previous", block.PreviousHash);
            command.Parameters.AddWithValue("$timestamp", block.Timestamp);
            command.Parameters.AddWithValue("$difficulty", block.Difficulty);
            command.Parameters.AddWithValue("$nonce", block.Nonce);
            command.ExecuteNonQuery();
        }

        for (var position = 0; position < block.Transactions.Count; position++)
        {
            var transaction = block.Transactions[position];
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO transactions (block_idx, position, id, sender, recipient, amount, fee, nonce, timestamp, public_key, signature)
                VALUES ($block, $position, $id, $from, $to, $amount, $fee, $nonce, $timestamp, $publicKey, $signature)
                """;
            command.Parameters.AddWithValue("$block", block.Index);
            command.Parameters.AddWithValue("$position", position);
            command.Parameters.AddWithValue("$id", transaction.Id);
            command.Parameters.AddWithValue("$from", transaction.From);
            command.Parameters.AddWithValue("$to", transaction.To);
            command.Parameters.AddWithValue("$amount", transaction.Amount);
            command.Parameters.AddWithValue("$fee", transaction.Fee);
            command.Parameters.AddWithValue("$nonce", transaction.Nonce);
            command.Parameters.AddWithValue("$timestamp", transaction.Timestamp);
            command.Parameters.AddWithValue("$publicKey", (object?)transaction.PublicKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$signature", (object?)transaction.Signature ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Schema;
        command.ExecuteNonQuery();
        return connection;
    }
}
