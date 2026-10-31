using System.Diagnostics.CodeAnalysis;

namespace MiniChain.Core;

/// <summary>An address is 40 lowercase hex characters (160 bits).</summary>
public static class Address
{
    public const int Length = 40;

    public static bool IsValid([NotNullWhen(true)] string? address)
    {
        if (address is null || address.Length != Length)
            return false;

        foreach (var c in address)
        {
            if (!char.IsAsciiHexDigitLower(c))
                return false;
        }
        return true;
    }
}
