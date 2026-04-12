using WiSave.Portal.Console.Execution;

namespace WiSave.Portal.Console.Shell;

internal interface IConsoleShell
{
    Task<int> RunAsync(CancellationToken ct);
}

internal sealed class ConsoleShell(
    ICommandCatalog commandCatalog,
    ICommandRunner commandRunner,
    IConsoleOutput consoleOutput) : IConsoleShell
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var commands = commandCatalog.List();
            RenderMenu(commands);

            consoleOutput.Write("Choose a command by number or name: ");
            var input = consoleOutput.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                consoleOutput.WriteLine("Enter a command, or type 'exit' to close the shell.");
                consoleOutput.WriteLine(string.Empty);
                continue;
            }

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                consoleOutput.Clear();
                continue;
            }

            if (input.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var descriptor = ResolveCommand(commands, input);
            if (descriptor is null)
            {
                consoleOutput.WriteLine($"Unknown selection '{input}'.");
                consoleOutput.WriteLine(string.Empty);
                continue;
            }

            consoleOutput.WriteLine(string.Empty);
            await commandRunner.RunAsync(new CommandInvocation(descriptor.Name, new Dictionary<string, string?>()), allowPrompting: true, ct);
            consoleOutput.WriteLine(string.Empty);
        }
    }

    private void RenderMenu(IReadOnlyList<CommandDescriptor> commands)
    {
        consoleOutput.WriteLine("WiSave Portal Console");
        consoleOutput.WriteLine("=====================");

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            consoleOutput.WriteLine($"{index + 1}. {command.Name} - {command.Description}");
        }

        consoleOutput.WriteLine(string.Empty);
        consoleOutput.WriteLine("Built-ins: help, clear, exit");
        consoleOutput.WriteLine(string.Empty);
    }

    private static CommandDescriptor? ResolveCommand(IReadOnlyList<CommandDescriptor> commands, string input)
    {
        if (int.TryParse(input, out var index) && index >= 1 && index <= commands.Count)
        {
            return commands[index - 1];
        }

        return commands.FirstOrDefault(command => command.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
    }
}
