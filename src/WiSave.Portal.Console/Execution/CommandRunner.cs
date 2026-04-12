using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Console.Shell;

namespace WiSave.Portal.Console.Execution;

internal interface ICommandRunner
{
    Task<int> RunAsync(CommandInvocation invocation, bool allowPrompting, CancellationToken ct);
}

internal sealed class CommandRunner(
    IServiceScopeFactory scopeFactory,
    ICommandCatalog commandCatalog,
    ICommandPrompter commandPrompter,
    IConsoleOutput consoleOutput) : ICommandRunner
{
    public async Task<int> RunAsync(CommandInvocation invocation, bool allowPrompting, CancellationToken ct)
    {
        var descriptor = commandCatalog.Find(invocation.CommandName);
        if (descriptor is null)
        {
            consoleOutput.WriteLine($"Unknown command '{invocation.CommandName}'.");
            PrintAvailableCommands();
            return 1;
        }

        var arguments = new Dictionary<string, string?>(invocation.Arguments, StringComparer.OrdinalIgnoreCase);
        if (arguments.ContainsKey("help"))
        {
            PrintUsage(descriptor);
            return 0;
        }

        if (allowPrompting)
        {
            arguments = await commandPrompter.PromptForMissingArgumentsAsync(descriptor, arguments, ct);
        }

        var missingRequiredParameters = descriptor.Parameters
            .Where(parameter =>
                parameter.Required &&
                (!arguments.TryGetValue(parameter.Name, out var value) || string.IsNullOrWhiteSpace(value)))
            .Select(parameter => parameter.Name)
            .ToArray();

        if (missingRequiredParameters.Length > 0)
        {
            consoleOutput.WriteLine("Missing required parameters:");
            foreach (var parameterName in missingRequiredParameters)
            {
                consoleOutput.WriteLine($"  --{parameterName}");
            }

            consoleOutput.WriteLine(string.Empty);
            PrintUsage(descriptor);
            return 1;
        }

        using var scope = scopeFactory.CreateScope();
        var command = scope.ServiceProvider.GetServices<IPortalCommand>()
            .FirstOrDefault(candidate => candidate.Name.Equals(descriptor.Name, StringComparison.OrdinalIgnoreCase));

        if (command is null)
        {
            consoleOutput.WriteLine($"Command '{descriptor.Name}' is registered in the catalog but could not be resolved.");
            return 1;
        }

        try
        {
            var result = await command.ExecuteAsync(new CommandExecutionContext(arguments), ct);
            PrintResult(result);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            consoleOutput.WriteLine($"Command '{descriptor.Name}' failed: {ex.Message}");
            return 1;
        }
    }

    private void PrintAvailableCommands()
    {
        foreach (var command in commandCatalog.List().OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase))
        {
            consoleOutput.WriteLine($"  {command.Name} - {command.Description}");
        }
    }

    private void PrintUsage(CommandDescriptor descriptor)
    {
        var usage = descriptor.Parameters.Count == 0
            ? descriptor.Name
            : $"{descriptor.Name} {string.Join(" ", descriptor.Parameters.Select(parameter => $"[--{parameter.Name} <value>]"))}";

        consoleOutput.WriteLine($"Usage: {usage}");
    }

    private void PrintResult(CommandResult result)
    {
        consoleOutput.WriteLine(result.Success ? $"OK: {result.Message}" : $"ERROR: {result.Message}");

        foreach (var detail in result.Details)
        {
            consoleOutput.WriteLine($"  {detail}");
        }
    }
}
