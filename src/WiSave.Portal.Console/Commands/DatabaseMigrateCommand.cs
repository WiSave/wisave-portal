using WiSave.Portal.Console.Execution;
using WiSave.Portal.Console.Operations;

namespace WiSave.Portal.Console.Commands;

internal sealed class DatabaseMigrateCommand(IDatabaseMigrationOperations migrationOperations) : IPortalCommand
{
    private static readonly IReadOnlyList<CommandParameter> Parameters =
    [
        new("connection-string", "Override the default portal connection string.", false)
    ];

    public string Name => "db-migrate";

    public string Description => "Apply portal database migrations.";

    public IReadOnlyList<CommandParameter> ParameterDefinitions => Parameters;

    public async Task<CommandResult> ExecuteAsync(CommandExecutionContext context, CancellationToken ct)
    {
        var connectionString = context.GetArgument("connection-string");
        var message = await migrationOperations.RunAsync(connectionString, ct);

        return CommandResult.SuccessResult(message);
    }
}
