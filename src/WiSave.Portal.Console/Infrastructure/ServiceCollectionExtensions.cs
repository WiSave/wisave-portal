using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Console.Execution;
using WiSave.Portal.Console.Operations;
using WiSave.Portal.Console.Shell;

namespace WiSave.Portal.Console.Infrastructure;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPortalConsole(this IServiceCollection services)
    {
        services.AddSingleton<IConsoleOutput, SystemConsoleOutput>();
        services.AddSingleton<ICommandCatalog, CommandCatalog>();
        services.AddSingleton<ICommandLineParser, CommandLineParser>();
        services.AddSingleton<ICommandPrompter, CommandPrompter>();
        services.AddSingleton<ICommandRunner, CommandRunner>();
        services.AddSingleton<IConsoleShell, ConsoleShell>();
        services.AddSingleton<IConsoleApplication, ConsoleApplication>();

        services.AddSingleton<IDatabaseMigrationOperations, DatabaseMigrationOperations>();
        services.AddSingleton<IDatabaseSeedOperations, DatabaseSeedOperations>();

        RegisterCommands(services, typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }

    private static void RegisterCommands(IServiceCollection services, Assembly assembly)
    {
        var commandTypes = assembly.GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                !type.IsInterface &&
                typeof(IPortalCommand).IsAssignableFrom(type))
            .ToArray();

        foreach (var commandType in commandTypes)
        {
            services.AddTransient(typeof(IPortalCommand), commandType);
        }
    }
}
