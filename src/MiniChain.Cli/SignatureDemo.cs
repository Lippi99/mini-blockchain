using MiniChain.Core;

namespace MiniChain.Cli;

/// <summary>The Phase 7 experiments: forge and tamper with signed transactions and watch verification fail.</summary>
internal static class SignatureDemo
{
    public static int Run()
    {
        var chain = new Blockchain();
        var mempool = new Mempool();
        using var accounts = new DemoAccounts();
        var (alice, bob, mallory) = (accounts["Alice"], accounts["Bob"], accounts["Mallory"]);

        chain.MinePendingTransactions(mempool, alice.Address);
        Console.WriteLine($"Alice mines block #1 and owns {chain.GetBalance(alice.Address)} coins. Her address is {alice.Address}.");
        Console.WriteLine();

        var payment = alice.CreateTransaction(bob.Address, amount: 10, fee: 1, nonce: 1, Now());
        Check("Alice signs a payment of 10 to Bob.", payment);

        var inflated = payment with { Amount = 40 };
        Check("Bob changes the amount of Alice's signed payment to 40.", inflated);
        Check("Bob also recomputes the id to match the new amount.", inflated with { Id = inflated.ComputeId() });

        Check("Mallory signs a payment from Alice's address with her own key.",
            mallory.Sign(new Transaction
            {
                From = alice.Address,
                To = mallory.Address,
                Amount = 50,
                Nonce = 1,
                Timestamp = Now(),
                PublicKey = mallory.PublicKeyHex,
            }));

        Check("Mallory copies Alice's public key into the transaction, but still signs with her own key.",
            mallory.Sign(new Transaction
            {
                From = alice.Address,
                To = mallory.Address,
                Amount = 50,
                Nonce = 1,
                Timestamp = Now(),
                PublicKey = alice.PublicKeyHex,
            }));

        Check("Someone submits a payment from Alice with no signature at all.",
            Transaction.Create(alice.Address, mallory.Address, amount: 50, fee: 0, nonce: 1, Now()));

        Console.WriteLine("Only the holder of Alice's private key can produce a signature that verifies against");
        Console.WriteLine("Alice's public key, and only that public key hashes to Alice's address.");
        return 0;

        void Check(string description, Transaction transaction)
        {
            Console.WriteLine(description);
            Console.WriteLine(TransactionValidator.Validate(transaction, out var error)
                ? "  -> valid: the signature verifies"
                : $"  -> rejected: {error}");
            Console.WriteLine();
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
