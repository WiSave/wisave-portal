using Microsoft.Extensions.DependencyInjection;

namespace WiSave.Portal.Console.Execution;

internal interface ICommandCatalog
{
    IReadOnlyList<CommandDescriptor> List();
    CommandDescriptor? Find(string name);
}

internal sealed class CommandCatalog(IServiceScopeFactory scopeFactory) : ICommandCatalog
{
    public IReadOnlyList<CommandDescriptor> List()
    {
        using var scope = scopeFactory.CreateScope();

        return scope.ServiceProvider.GetServices<IPortalCommand>()
            .Select(command => new CommandDescriptor(command.Name, command.Description, command.ParameterDefinitions))
            .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CommandDescriptor? Find(string name)
        => List().FirstOrDefault(command => command.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
