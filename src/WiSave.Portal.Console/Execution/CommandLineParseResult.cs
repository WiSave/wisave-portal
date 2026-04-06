namespace WiSave.Portal.Console.Execution;

internal sealed class CommandLineParseResult
{
    private CommandLineParseResult(bool isInteractive, CommandInvocation? invocation, string? errorMessage)
    {
        IsInteractive = isInteractive;
        Invocation = invocation;
        ErrorMessage = errorMessage;
    }

    public bool IsInteractive { get; }

    public CommandInvocation? Invocation { get; }

    public string? ErrorMessage { get; }

    public static CommandLineParseResult Interactive() => new(true, null, null);

    public static CommandLineParseResult Success(CommandInvocation invocation) => new(false, invocation, null);

    public static CommandLineParseResult Failure(string errorMessage) => new(false, null, errorMessage);
}
