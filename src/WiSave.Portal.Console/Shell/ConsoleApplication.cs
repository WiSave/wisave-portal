using WiSave.Portal.Console.Execution;

namespace WiSave.Portal.Console.Shell;

internal interface IConsoleApplication
{
    Task<int> RunAsync(string[] args, CancellationToken ct);
}

internal sealed class ConsoleApplication(
    ICommandLineParser commandLineParser,
    ICommandRunner commandRunner,
    ICommandCatalog commandCatalog,
    IConsoleShell consoleShell,
    IConsoleOutput consoleOutput) : IConsoleApplication
{
    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 1 &&
            (args[0].Equals("help", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            consoleOutput.WriteLine("Available commands:");
            foreach (var command in commandCatalog.List())
            {
                consoleOutput.WriteLine($"  {command.Name} - {command.Description}");
            }

            consoleOutput.WriteLine(string.Empty);
            consoleOutput.WriteLine("Run without arguments to start the interactive shell.");
            return 0;
        }

        var parseResult = commandLineParser.Parse(args);
        if (parseResult.IsInteractive)
        {
            return await consoleShell.RunAsync(ct);
        }

        if (parseResult.Invocation is null)
        {
            consoleOutput.WriteLine(parseResult.ErrorMessage ?? "Failed to parse command line arguments.");
            return 1;
        }

        return await commandRunner.RunAsync(parseResult.Invocation, allowPrompting: false, ct);
    }
}
