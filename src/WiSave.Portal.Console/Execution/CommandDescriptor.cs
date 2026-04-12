namespace WiSave.Portal.Console.Execution;

internal sealed record CommandDescriptor(
    string Name,
    string Description,
    IReadOnlyList<CommandParameter> Parameters);
