namespace WiSave.Portal.Console.Execution;

internal sealed class CommandResult
{
    private CommandResult(bool success, string message, IReadOnlyList<string>? details = null)
    {
        Success = success;
        Message = message;
        Details = details ?? [];
    }

    public bool Success { get; }

    public string Message { get; }

    public IReadOnlyList<string> Details { get; }

    public static CommandResult SuccessResult(string message, IReadOnlyList<string>? details = null)
        => new(true, message, details);

    public static CommandResult FailureResult(string message, IReadOnlyList<string>? details = null)
        => new(false, message, details);
}
