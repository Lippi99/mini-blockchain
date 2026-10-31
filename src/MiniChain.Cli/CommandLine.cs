using System.Globalization;

namespace MiniChain.Cli;

/// <summary>A user mistake on the command line. The message is printed without a stack trace.</summary>
internal sealed class CliUsageException(string message) : Exception(message);

/// <summary>
/// A tiny parser for <c>--name value</c> options. An option followed by another option, or by
/// nothing, is a flag without a value. Anything else is a positional argument.
/// </summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _arguments = [];

    public IReadOnlyList<string> Arguments => _arguments;

    /// <summary>The first positional argument, such as the "new" in "wallet new", if there is one.</summary>
    public string? FirstArgument => _arguments.Count > 0 ? _arguments[0] : null;

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var result = new CommandLine();
        for (var i = 0; i < args.Count; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                result._arguments.Add(args[i]);
                continue;
            }

            var name = args[i][2..];
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            result._options[name] = hasValue ? args[++i] : null;
        }
        return result;
    }

    /// <summary>The option's value, or null if the option is missing or was given without a value.</summary>
    public string? Get(string name) => _options.GetValueOrDefault(name);

    public string Require(string name) =>
        Get(name) ?? throw new CliUsageException($"Missing required option --{name} <value>.");

    public bool Has(string name) => _options.ContainsKey(name);

    public int GetInt(string name, int defaultValue)
    {
        var value = GetLong(name, defaultValue);
        return value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : throw new CliUsageException($"Option --{name} is too large.");
    }

    public long GetLong(string name, long defaultValue)
    {
        if (Has(name) && Get(name) is null)
            throw new CliUsageException($"Option --{name} needs a value.");

        var value = Get(name);
        if (value is null)
            return defaultValue;

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new CliUsageException($"Option --{name} must be a whole number, got '{value}'.");
    }
}
