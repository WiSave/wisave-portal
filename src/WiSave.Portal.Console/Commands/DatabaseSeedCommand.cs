using WiSave.Portal.Console.Execution;
using WiSave.Portal.Console.Operations;

namespace WiSave.Portal.Console.Commands;

internal sealed class DatabaseSeedCommand(IDatabaseSeedOperations seedOperations) : IPortalCommand
{
    private static readonly IReadOnlyList<CommandParameter> Parameters =
    [
        new("connection-string", "Override the default portal connection string.", false)
    ];

    public string Name => "db-seed";

    public string Description => "Apply portal database seed scripts.";

    public IReadOnlyList<CommandParameter> ParameterDefinitions => Parameters;

    public async Task<CommandResult> ExecuteAsync(CommandExecutionContext context, CancellationToken ct)
    {
        var connectionString = context.GetArgument("connection-string");
        var message = await seedOperations.RunAsync(connectionString, ct);

        return CommandResult.SuccessResult(message);
    }
}
