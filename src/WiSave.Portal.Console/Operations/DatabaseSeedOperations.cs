using Microsoft.Extensions.Configuration;
using WiSave.Portal.Migrations;

namespace WiSave.Portal.Console.Operations;

internal interface IDatabaseSeedOperations
{
    Task<string> RunAsync(string? connectionString, CancellationToken ct);
}

internal sealed class DatabaseSeedOperations(IConfiguration configuration) : IDatabaseSeedOperations
{
    public Task<string> RunAsync(string? connectionString, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var effectiveConnectionString = connectionString;
        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            effectiveConnectionString = configuration.GetConnectionString("Portal");
        }

        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            throw new InvalidOperationException(
                "Portal connection string was not configured. Set ConnectionStrings__Portal or appsettings.json.");
        }

        DbMigrator.RunSeeds(effectiveConnectionString);

        return Task.FromResult("Portal database seeds applied.");
    }
}
