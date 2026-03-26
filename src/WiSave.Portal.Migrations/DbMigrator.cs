using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace WiSave.Portal.Migrations;

public static class DbMigrator
{
    public static DatabaseUpgradeResult ApplyChanges(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                scriptName => scriptName.Contains(".Scripts."))
            .WithVariablesDisabled()
            .WithoutTransaction()
            .LogToConsole()
            .Build();

        return upgrader.PerformUpgrade();
    }

    public static void Run(string connectionString)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        var result = ApplyChanges(connectionString);
        if (!result.Successful)
        {
            throw new Exception("Database migration failed", result.Error);
        }

        Console.WriteLine("Success!");
    }
}
