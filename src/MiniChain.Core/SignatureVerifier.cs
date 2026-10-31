using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace MiniChain.Core;

/// <summary>Checks that a transaction was authorized by the owner of its sender address.</summary>
public static class SignatureVerifier
{
    // Every uncompressed NIST P-256 public key in SubjectPublicKeyInfo (DER) form is 91 bytes and
    // starts with these 26 bytes: SEQUENCE { SEQUENCE { OID id-ecPublicKey, OID prime256v1 },
    // BIT STRING {...} }. The remaining 65 bytes are 0x04 followed by the X and Y coordinates.
    private static ReadOnlySpan<byte> P256Prefix =>
        [0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01,
         0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00];

    private const int P256PublicKeyLength = 91;

    public static bool IsP256PublicKey(ReadOnlySpan<byte> publicKey) =>
        publicKey.Length == P256PublicKeyLength && publicKey.StartsWith(P256Prefix);

    /// <summary>
    /// Verifies that the public key belongs to <see cref="Transaction.From"/> and that the signature
    /// over the id bytes was made with the matching private key. The caller has already checked that
    /// the id matches the transaction's contents.
    /// </summary>
    public static bool Verify(Transaction transaction, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrEmpty(transaction.PublicKey) || string.IsNullOrEmpty(transaction.Signature))
        {
            error = "the transaction is not signed.";
            return false;
        }

        byte[] publicKey, signature;
        try
        {
            publicKey = Convert.FromHexString(transaction.PublicKey);
            signature = Convert.FromHexString(transaction.Signature);
        }
        catch (FormatException)
        {
            error = "the public key or the signature is not valid hex.";
            return false;
        }

        // This check is what stops anyone from spending from an address whose key they do not hold.
        if (Address.FromPublicKey(publicKey) != transaction.From)
        {
            error = "the public key does not belong to the sender's address.";
            return false;
        }
        if (!IsP256PublicKey(publicKey))
        {
            error = "the public key is not an uncompressed NIST P-256 key.";
            return false;
        }

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        }
        catch (CryptographicException)
        {
            error = "the public key is not a valid point on the curve.";
            return false;
        }

        if (!ecdsa.VerifyData(Convert.FromHexString(transaction.Id), signature, HashAlgorithmName.SHA256))
        {
            error = "the signature is not valid.";
            return false;
        }

        error = null;
        return true;
    }
}
