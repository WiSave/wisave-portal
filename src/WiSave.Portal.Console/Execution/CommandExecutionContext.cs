namespace WiSave.Portal.Console.Execution;

internal sealed class CommandExecutionContext
{
    public CommandExecutionContext(IReadOnlyDictionary<string, string?> arguments)
    {
        Arguments = new Dictionary<string, string?>(arguments, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string?> Arguments { get; }

    public string? GetArgument(string name)
        => Arguments.TryGetValue(name, out var value) ? value : null;
}
