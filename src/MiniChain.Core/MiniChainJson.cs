using System.Text.Json;

namespace MiniChain.Core;

/// <summary>
/// The JSON settings nodes and clients use for blocks and transactions. Hashes never depend on JSON,
/// but data from other nodes is untrusted, so missing or null required fields are rejected while
/// parsing instead of surfacing later as null references.
/// </summary>
public static class MiniChainJson
{
    /// <summary>camelCase web defaults plus the strict settings from <see cref="Configure"/>.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>Applies the strict settings to an existing options instance, such as ASP.NET Core's.</summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
