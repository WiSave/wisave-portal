namespace WiSave.Portal.Console.Execution;

internal sealed class CommandInvocation
{
    public CommandInvocation(string commandName, IReadOnlyDictionary<string, string?> arguments)
    {
        CommandName = commandName;
        Arguments = new Dictionary<string, string?>(arguments, StringComparer.OrdinalIgnoreCase);
    }

    public string CommandName { get; }

    public IReadOnlyDictionary<string, string?> Arguments { get; }
}
