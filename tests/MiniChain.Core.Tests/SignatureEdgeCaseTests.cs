using System.Globalization;
using System.Security.Cryptography;

namespace MiniChain.Core.Tests;

/// <summary>Malformed keys and signatures must be rejected, never crash validation.</summary>
public sealed class SignatureEdgeCaseTests : IDisposable
{
    private static readonly TestAccount Alice = TestAccounts.Alice;
    private static readonly TestAccount Bob = TestAccounts.Bob;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "minichain-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>A transfer "signed" with <paramref name="publicKey"/>, from the address those exact bytes hash to.</summary>
    private static Transaction FromKey(byte[] publicKey)
    {
        var transaction = new Transaction
        {
            From = Address.FromPublicKey(publicKey),
            To = Bob.Address,
            Amount = 1,
            Nonce = 1,
            PublicKey = Convert.ToHexStringLower(publicKey),
            Signature = new string('a', 128),
        };
        return transaction with { Id = transaction.ComputeId() };
    }

    private static byte[] OffCurveKey()
    {
        var key = Alice.Wallet.PublicKey;
        key[^1] ^= 1; // Flip one bit of Y: the point is no longer on the curve.
        return key;
    }

    [Fact]
    public void A_public_key_that_is_not_on_the_curve_is_rejected_instead_of_throwing()
    {
        var transaction = FromKey(OffCurveKey());

        Assert.False(TransactionValidator.Validate(transaction, out var error));
        Assert.Equal("the public key is not a valid point on the curve.", error);
    }

    [Fact]
    public void An_off_curve_key_cannot_crash_the_mempool_or_block_validation()
    {
        var chain = TestChains.Create();
        TestChains.MineBlock(chain, Alice);
        var transaction = FromKey(OffCurveKey());

        Assert.False(new Mempool().TryAdd(transaction, chain, out var mempoolError));
        Assert.Contains("not a valid point on the curve", mempoolError);

        var block = chain.CreateCandidateBlock(Bob.Address, [transaction], Genesis.Timestamp);
        Assert.False(TestChains.TryMineAndAdd(chain, block, out var blockError));
        Assert.Contains("not a valid point on the curve", blockError);
        Assert.False(ChainValidator.ValidateChain([.. chain.Blocks, block], chain.Parameters, out _));
    }

    [Fact]
    public void A_compressed_p256_key_is_rejected()
    {
        // The same point in compressed form: 0x02 or 0x03 (Y's parity) followed by X only.
        var uncompressed = Alice.Wallet.PublicKey;
        var x = uncompressed.AsSpan(27, 32);
        byte[] compressed =
        [
            0x30, 0x39, 0x30, 0x13, 0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01,
            0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07, 0x03, 0x22, 0x00,
            (byte)(0x02 | (uncompressed[^1] & 1)), .. x,
        ];

        Assert.False(SignatureVerifier.IsP256PublicKey(compressed));
        Assert.False(TransactionValidator.Validate(FromKey(compressed), out var error));
        Assert.Equal("the public key is not an uncompressed NIST P-256 key.", error);
    }

    [Fact]
    public void A_key_with_trailing_bytes_is_rejected()
    {
        byte[] padded = [.. Alice.Wallet.PublicKey, 0x00];

        Assert.False(SignatureVerifier.IsP256PublicKey(padded));
        Assert.False(TransactionValidator.Validate(FromKey(padded), out var error));
        Assert.Equal("the public key is not an uncompressed NIST P-256 key.", error);
    }

    [Fact]
    public void An_empty_signature_on_a_coinbase_counts_as_no_signature_just_like_in_the_hash()
    {
        var chain = TestChains.Create(blocks: 1);
        var block = chain.Blocks[1];
        var hashBefore = block.ComputeHash();

        block.Transactions = [block.Transactions[0] with { Signature = "", PublicKey = "" }];

        // Same hash, so the block must still be valid: an edit that keeps the hash may not change the verdict.
        Assert.Equal(hashBefore, block.ComputeHash());
        Assert.True(chain.IsValid(out var error), error);
    }

    [Fact]
    public void The_id_covers_the_public_key_and_negative_numbers_in_the_canonical_format()
    {
        // SHA-256 of "{40 x '1'}|{40 x '2'}|-5|0|-1|-7|abcd", computed with `sha256sum`.
        var transaction = new Transaction
        {
            From = new string('1', 40),
            To = new string('2', 40),
            Amount = -5,
            Fee = 0,
            Nonce = -1,
            Timestamp = -7,
            PublicKey = "abcd",
        };

        var original = CultureInfo.CurrentCulture;
        var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        odd.NumberFormat.NegativeSign = "~";
        try
        {
            CultureInfo.CurrentCulture = odd;
            Assert.Equal("00b02a9dc413eb724b64a0589d1967c55fb1bb07a0c78c087a5c5e816142ee17", transaction.ComputeId());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_wallet_file_with_only_a_public_key_is_rejected()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "public-only.pem");
        File.WriteAllText(path, Alice.Wallet.PublicKeyPem());

        var error = Assert.ThrowsAny<CryptographicException>(() => Wallet.Load(path));
        Assert.Contains("holds only a public key", error.Message);
    }

    [Fact]
    public void A_wallet_file_on_another_curve_is_rejected()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "p384.pem");
        using (var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384))
            File.WriteAllText(path, p384.ExportECPrivateKeyPem());

        var error = Assert.ThrowsAny<CryptographicException>(() => Wallet.Load(path));
        Assert.Contains("NIST P-256", error.Message);
    }
}

internal static class WalletTestExtensions
{
    /// <summary>The wallet's public key as PEM, which is what a key file without the private part looks like.</summary>
    public static string PublicKeyPem(this Wallet wallet) =>
        PemEncoding.WriteString("PUBLIC KEY", wallet.PublicKey);
}
