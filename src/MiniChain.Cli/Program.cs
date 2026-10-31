using MiniChain.Cli;

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    PrintHelp();
    return 0;
}

switch (args[0])
{
    case "demo":
        return ChainDemo.Run();
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        PrintHelp();
        return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
        MiniChain CLI

        Usage: dotnet run --project src/MiniChain.Cli -- <command>

        Commands:
          demo      Build a small chain and print it
        """);
}
