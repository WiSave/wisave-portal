using System.Reflection;
using DbUp;
using DbUp.Engine;
using Npgsql;

namespace WiSave.Portal.Migrations;

public static class DbMigrator
{
    public const string JournalSchema = "public";
    public const string JournalTableName = "SchemaVersions";

    public static DatabaseUpgradeResult ApplyChanges(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable(JournalSchema, JournalTableName)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                scriptName => scriptName.Contains(".Scripts."))
            .WithVariablesDisabled()
            .WithoutTransaction()
            .LogToConsole()
            .Build();

        return upgrader.PerformUpgrade();
    }


    public static DatabaseUpgradeResult Run(string connectionString)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);
        EnsureJournalSchemaExists(connectionString);

        var result = ApplyChanges(BuildScopedConnectionString(connectionString));
        return !result.Successful ? throw new Exception("Database migration failed", result.Error) : result;
    }

    private static void EnsureJournalSchemaExists(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA IF NOT EXISTS {JournalSchema};";
        command.ExecuteNonQuery();
    }

    private static string BuildScopedConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = JournalSchema
        };

        return builder.ConnectionString;
    }
}