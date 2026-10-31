using System.Security.Cryptography;

namespace MiniChain.Core.Tests;

public sealed class WalletTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "minichain-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string KeyFile(string name = "wallet") => Path.Combine(_directory, $"{name}.pem");

    [Fact]
    public void The_address_is_derived_from_the_public_key()
    {
        using var wallet = new Wallet();

        Assert.Equal(HashUtil.Sha256Hex(wallet.PublicKey)[..40], wallet.Address);
        Assert.True(Address.IsValid(wallet.Address));
        Assert.Equal(Convert.ToHexStringLower(wallet.PublicKey), wallet.PublicKeyHex);
        Assert.True(SignatureVerifier.IsP256PublicKey(wallet.PublicKey));
    }

    [Fact]
    public void Every_wallet_gets_its_own_key()
    {
        using var first = new Wallet();
        using var second = new Wallet();

        Assert.NotEqual(first.Address, second.Address);
    }

    [Fact]
    public void A_saved_wallet_loads_with_the_same_key()
    {
        using var original = new Wallet();
        original.Save(KeyFile());

        using var loaded = Wallet.Load(KeyFile());

        Assert.Equal(original.Address, loaded.Address);
        var payment = loaded.CreateTransaction(original.Address, 1, 0, 1, 0);
        Assert.True(TransactionValidator.Validate(payment, out var error), error);
        Assert.Contains("BEGIN EC PRIVATE KEY", File.ReadAllText(KeyFile()));
    }

    [Fact]
    public void An_encrypted_wallet_needs_the_right_password()
    {
        using var original = new Wallet();
        original.Save(KeyFile(), password: "correct horse");

        var contents = File.ReadAllText(KeyFile());
        Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", contents);
        Assert.DoesNotContain("BEGIN EC PRIVATE KEY", contents);

        Assert.ThrowsAny<CryptographicException>(() => Wallet.Load(KeyFile(), password: "wrong"));
        Assert.ThrowsAny<ArgumentException>(() => Wallet.Load(KeyFile()));
        using var loaded = Wallet.Load(KeyFile(), password: "correct horse");
        Assert.Equal(original.Address, loaded.Address);
    }

    [Fact]
    public void Saving_never_overwrites_an_existing_key_file()
    {
        using var first = new Wallet();
        using var second = new Wallet();
        first.Save(KeyFile());

        Assert.Throws<IOException>(() => second.Save(KeyFile()));

        using var loaded = Wallet.Load(KeyFile());
        Assert.Equal(first.Address, loaded.Address);
    }

    [Fact]
    public void ToString_does_not_reveal_any_key_material()
    {
        using var wallet = new Wallet();

        Assert.Equal($"Wallet {wallet.Address}", wallet.ToString());
    }
}
