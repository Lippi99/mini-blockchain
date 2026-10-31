using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace MiniChain.Core;

/// <summary>
/// An address is 40 lowercase hex characters (160 bits): the start of the SHA-256 hash of a public key.
/// </summary>
public static class Address
{
    public const int Length = 40;

    /// <summary>The address that belongs to a public key in SubjectPublicKeyInfo form.</summary>
    public static string FromPublicKey(ReadOnlySpan<byte> publicKey) =>
        Convert.ToHexStringLower(SHA256.HashData(publicKey))[..Length];

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
