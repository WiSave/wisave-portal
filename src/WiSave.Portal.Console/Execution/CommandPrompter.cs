using WiSave.Portal.Console.Shell;

namespace WiSave.Portal.Console.Execution;

internal interface ICommandPrompter
{
    Task<Dictionary<string, string?>> PromptForMissingArgumentsAsync(
        CommandDescriptor descriptor,
        IReadOnlyDictionary<string, string?> existingArguments,
        CancellationToken ct);
}

internal sealed class CommandPrompter(IConsoleOutput consoleOutput) : ICommandPrompter
{
    public Task<Dictionary<string, string?>> PromptForMissingArgumentsAsync(
        CommandDescriptor descriptor,
        IReadOnlyDictionary<string, string?> existingArguments,
        CancellationToken ct)
    {
        var arguments = new Dictionary<string, string?>(existingArguments, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in descriptor.Parameters)
        {
            if (arguments.TryGetValue(parameter.Name, out var currentValue) && !string.IsNullOrWhiteSpace(currentValue))
            {
                continue;
            }

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var label = $"Enter {parameter.Name}";
                if (!string.IsNullOrWhiteSpace(parameter.Description))
                {
                    label += $" ({parameter.Description})";
                }

                if (!string.IsNullOrWhiteSpace(parameter.DefaultValue))
                {
                    label += $" [{parameter.DefaultValue}]";
                }
                else if (!parameter.Required)
                {
                    label += " [optional]";
                }

                label += ": ";
                consoleOutput.Write(label);

                var input = consoleOutput.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (!string.IsNullOrWhiteSpace(parameter.DefaultValue))
                    {
                        arguments[parameter.Name] = parameter.DefaultValue;
                        break;
                    }

                    if (!parameter.Required)
                    {
                        break;
                    }

                    consoleOutput.WriteLine($"Parameter '{parameter.Name}' is required.");
                    continue;
                }

                arguments[parameter.Name] = input;
                break;
            }
        }

        return Task.FromResult(arguments);
    }
}
