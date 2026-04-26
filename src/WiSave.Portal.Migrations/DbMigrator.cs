using System.Reflection;
using DbUp;
using DbUp.Engine;
using Npgsql;

namespace WiSave.Portal.Migrations;

public static class DbMigrator
{
    public const string JournalSchema = "public";
    public const string JournalTableName = "SchemaVersions";
    public const string SeedJournalTableName = "SeedVersions";

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

    public static DatabaseUpgradeResult ApplySeeds(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable(JournalSchema, SeedJournalTableName)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                scriptName => scriptName.Contains(".Seeds."))
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

    public static DatabaseUpgradeResult RunSeeds(string connectionString)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);
        EnsureJournalSchemaExists(connectionString);
        EnsureIdentitySchemaExists(connectionString);

        var result = ApplySeeds(BuildScopedConnectionString(connectionString));
        return !result.Successful ? throw new Exception("Database seeding failed", result.Error) : result;
    }

    private static void EnsureJournalSchemaExists(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA IF NOT EXISTS {JournalSchema};";
        command.ExecuteNonQuery();
    }

    private static void EnsureIdentitySchemaExists(string connectionString)
    {
        using var connection = new NpgsqlConnection(BuildScopedConnectionString(connectionString));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT COUNT(*) = 2
                              FROM information_schema.tables
                              WHERE table_schema = 'public'
                                AND table_name IN ('AspNetRoles', 'AspNetRoleClaims');
                              """;

        if (command.ExecuteScalar() is not true)
            throw new InvalidOperationException("Portal Identity schema was not found. Run db-migrate before db-seed.");
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
