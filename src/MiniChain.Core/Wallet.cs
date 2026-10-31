using System.Security.Cryptography;
using System.Text;

namespace MiniChain.Core;

/// <summary>
/// A key pair on the NIST P-256 curve. The private key signs transactions and must stay secret; the
/// public key lets anyone verify those signatures; the address is a short identifier derived from
/// the public key.
/// </summary>
public sealed class Wallet : IDisposable
{
    private readonly ECDsa _key;

    /// <summary>Creates a wallet with a new random key pair.</summary>
    public Wallet() : this(ECDsa.Create(ECCurve.NamedCurves.nistP256))
    {
    }

    private Wallet(ECDsa key)
    {
        _key = key;
        var publicKey = key.ExportSubjectPublicKeyInfo();
        if (!SignatureVerifier.IsP256PublicKey(publicKey))
            throw new CryptographicException("MiniChain wallets must use the NIST P-256 curve.");

        PublicKeyHex = Convert.ToHexStringLower(publicKey);
        Address = MiniChain.Core.Address.FromPublicKey(publicKey);
    }

    /// <summary>The public key in SubjectPublicKeyInfo (DER) form.</summary>
    public byte[] PublicKey => _key.ExportSubjectPublicKeyInfo();

    /// <summary>The public key as it appears in <see cref="Transaction.PublicKey"/>.</summary>
    public string PublicKeyHex { get; }

    public string Address { get; }

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256);

    /// <summary>Fills in <paramref name="transaction"/>'s id and signs the id bytes.</summary>
    public Transaction Sign(Transaction transaction)
    {
        var id = transaction.ComputeId();
        return transaction with { Id = id, Signature = Convert.ToHexStringLower(Sign(Convert.FromHexString(id))) };
    }

    /// <summary>Creates a signed transfer from this wallet.</summary>
    public Transaction CreateTransaction(string to, long amount, long fee, long nonce, long timestamp) =>
        Sign(new Transaction
        {
            From = Address,
            To = to,
            Amount = amount,
            Fee = fee,
            Nonce = nonce,
            Timestamp = timestamp,
            PublicKey = PublicKeyHex,
        });

    /// <summary>
    /// Saves the private key as a PEM file. Without a password the key is stored in plain text, so
    /// keep the file out of Git (the repository's .gitignore excludes <c>wallets/</c> and <c>*.pem</c>).
    /// An existing file is never overwritten, because that would destroy the key it holds.
    /// </summary>
    public void Save(string path, string? password = null)
    {
        var pem = password is null
            ? _key.ExportECPrivateKeyPem()
            : _key.ExportEncryptedPkcs8PrivateKeyPem(password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, iterationCount: 600_000));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // Readable by the owner only.

        using var writer = new StreamWriter(path, Encoding.ASCII, options);
        writer.Write(pem);
    }

    /// <summary>Loads a wallet saved by <see cref="Save"/>.</summary>
    public static Wallet Load(string path, string? password = null)
    {
        var pem = File.ReadAllText(path);
        var key = ECDsa.Create();
        try
        {
            if (password is null)
                key.ImportFromPem(pem);
            else
                key.ImportFromEncryptedPem(pem, password);
            return new Wallet(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public void Dispose() => _key.Dispose();

    // Never include key material in a string that might end up in a log.
    public override string ToString() => $"Wallet {Address}";
}
