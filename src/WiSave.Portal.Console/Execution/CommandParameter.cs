namespace WiSave.Portal.Console.Execution;

internal sealed record CommandParameter(
    string Name,
    string Description,
    bool Required,
    string? DefaultValue = null);
