using System.Security.Cryptography;
using System.Text;

namespace MiniChain.Core;

/// <summary>SHA-256 helpers. Hashes are always rendered as lowercase hex.</summary>
public static class HashUtil
{
    public static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));
}
