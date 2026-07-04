using ScriptWarden;
using ScriptWarden.Commands;

// Entry point + command dispatch. Kept dependency-free (hand-rolled parsing) for a small, fast,
// AOT-friendly binary.
if (args.Length == 0)
{
    Help.Print();
    return 0;
}

string command = args[0].ToLowerInvariant().TrimStart('-');

try
{
    switch (command)
    {
        case "shim":
            return ShimCommand.Run(args);

        case "install":
            return RegistryCommands.Install(args);

        case "uninstall":
            return RegistryCommands.Uninstall(args);

        case "status":
            return RegistryCommands.Status(args);

        case "list":
            return ListCommand.Run(args);

        case "clear":
            return ClearCommand.Run(args);

        case "analyze":
            return AnalyzeCommand.Run(args);

        case "config":
            return ConfigCommand.Run(args);

        case "diagnose":
            return DiagnoseCommand.Run(args);

        case "serve":
            return ScriptWarden.Web.ServeCommand.Run(args);

        case "help":
        case "h":
        case "?":
            Help.Print();
            return 0;

        case "version":
        case "v":
            Help.PrintVersion();
            return 0;

        default:
            Console.Error.WriteLine($"script-warden: unknown command '{args[0]}'.");
            Help.Print();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"script-warden: {ex.Message}");
    return 1;
}
