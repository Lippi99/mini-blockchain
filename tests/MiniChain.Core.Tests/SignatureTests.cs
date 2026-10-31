using System.Security.Cryptography;

namespace MiniChain.Core.Tests;

public class SignatureTests
{
    private static readonly TestAccount Alice = TestAccounts.Alice;
    private static readonly TestAccount Bob = TestAccounts.Bob;
    private static readonly TestAccount Mallory = TestAccounts.Mallory;

    private static string Rejected(Transaction transaction)
    {
        Assert.False(TransactionValidator.Validate(transaction, out var error));
        return error;
    }

    [Fact]
    public void A_correctly_signed_transaction_is_valid()
    {
        var payment = Alice.Pay(Bob, 10, nonce: 1, fee: 1);

        Assert.Equal(Alice.Wallet.PublicKeyHex, payment.PublicKey);
        Assert.True(TransactionValidator.Validate(payment, out var error), error);
    }

    [Fact]
    public void An_unsigned_transaction_is_rejected()
    {
        var unsigned = Transaction.Create(Alice.Address, Bob.Address, 10, 0, 1, 0);

        Assert.Equal("the transaction is not signed.", Rejected(unsigned));
    }

    [Fact]
    public void Changing_the_amount_of_a_signed_transaction_is_detected()
    {
        var payment = Alice.Pay(Bob, 10, nonce: 1);

        var inflated = payment with { Amount = 40 };
        Assert.Contains("id does not match", Rejected(inflated));

        // Recomputing the id does not help: the signature covers the original id.
        Assert.Equal("the signature is not valid.", Rejected(inflated with { Id = inflated.ComputeId() }));
    }

    [Fact]
    public void Signing_with_one_wallet_while_claiming_another_address_is_rejected()
    {
        var forged = Mallory.Wallet.Sign(new Transaction
        {
            From = Alice.Address,
            To = Mallory.Address,
            Amount = 50,
            Nonce = 1,
            PublicKey = Mallory.Wallet.PublicKeyHex,
        });

        Assert.Equal("the public key does not belong to the sender's address.", Rejected(forged));
    }

    [Fact]
    public void Copying_the_victims_public_key_does_not_help_without_the_private_key()
    {
        var forged = Mallory.Wallet.Sign(new Transaction
        {
            From = Alice.Address,
            To = Mallory.Address,
            Amount = 50,
            Nonce = 1,
            PublicKey = Alice.Wallet.PublicKeyHex,
        });

        Assert.Equal("the signature is not valid.", Rejected(forged));
    }

    [Fact]
    public void A_signature_cannot_be_moved_to_another_transaction()
    {
        var small = Alice.Pay(Bob, 1, nonce: 1);
        var large = Alice.Pay(Bob, 49, nonce: 1);

        Assert.Equal("the signature is not valid.", Rejected(large with { Signature = small.Signature }));
    }

    [Fact]
    public void Malformed_keys_and_signatures_are_rejected()
    {
        var payment = Alice.Pay(Bob, 10, nonce: 1);

        Assert.Equal("the public key or the signature is not valid hex.",
            Rejected(Resign(payment with { PublicKey = "not hex" })));
        Assert.Equal("the public key or the signature is not valid hex.",
            Rejected(payment with { Signature = "xyz" }));
        Assert.Equal("the signature is not valid.",
            Rejected(payment with { Signature = "abcd" }));
    }

    [Fact]
    public void A_key_on_another_curve_is_rejected()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var publicKey = p384.ExportSubjectPublicKeyInfo();
        var transaction = new Transaction
        {
            From = Address.FromPublicKey(publicKey),
            To = Bob.Address,
            Amount = 1,
            Nonce = 1,
            PublicKey = Convert.ToHexStringLower(publicKey),
        };
        var id = transaction.ComputeId();
        var signed = transaction with
        {
            Id = id,
            Signature = Convert.ToHexStringLower(p384.SignData(Convert.FromHexString(id), HashAlgorithmName.SHA256)),
        };

        Assert.Equal("the public key is not an uncompressed NIST P-256 key.", Rejected(signed));
    }

    [Fact]
    public void A_block_with_an_unsigned_transfer_is_rejected()
    {
        var chain = TestChains.Create();
        TestChains.MineBlock(chain, Alice);
        var block = chain.CreateCandidateBlock(Bob.Address,
            [Transaction.Create(Alice.Address, Bob.Address, 10, 0, 1, Genesis.Timestamp)], Genesis.Timestamp);

        Assert.False(TestChains.TryMineAndAdd(chain, block, out var error));
        Assert.Contains("the transaction is not signed.", error);
    }

    /// <summary>Recomputes the id after an edit, so the test reaches the check it is aimed at.</summary>
    private static Transaction Resign(Transaction transaction) => transaction with { Id = transaction.ComputeId() };
}
