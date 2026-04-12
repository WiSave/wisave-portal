namespace WiSave.Portal.Console.Execution;

internal interface IPortalCommand
{
    string Name { get; }

    string Description { get; }

    IReadOnlyList<CommandParameter> ParameterDefinitions { get; }

    Task<CommandResult> ExecuteAsync(CommandExecutionContext context, CancellationToken ct);
}
