using System.Text.Json;
using MiniChain.Core;

namespace MiniChain.Node.Storage;

/// <summary>
/// The plan's "simple option": the whole chain as one readable JSON file, rewritten after every change.
/// Open it in an editor, change an amount, and the node will refuse to start.
/// </summary>
public sealed class JsonChainStore(string path) : IChainStore
{
    private static readonly JsonSerializerOptions FileOptions = new(MiniChainJson.Options) { WriteIndented = true };

    public string Location => path;

    public IReadOnlyList<Block>? Load()
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<Block>>(File.ReadAllText(path), FileOptions)
                ?? throw new InvalidDataException($"{path} contains no blocks.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path} is not a valid chain file: {e.Message}", e);
        }
    }

    public void Save(IReadOnlyList<Block> blocks)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temporary file and then swap it in, so a crash halfway through never leaves half a chain.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(blocks, FileOptions));
        File.Move(temporary, path, overwrite: true);
    }
}
