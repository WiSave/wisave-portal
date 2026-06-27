using Microsoft.Extensions.Configuration;
using Xunit;

namespace WiSave.Portal.UnitTests.Messaging;

public sealed class RabbitMqConfigurationTests
{
    [Fact]
    public void AppSettings_UsesPortalRabbitMqVirtualHostByDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepoPath("src/WiSave.Portal/appsettings.json"))
            .Build();

        Assert.Equal("portal", configuration["RabbitMq:VirtualHost"]);
    }

    [Fact]
    public void AppSettings_ConfiguresIncomesAndExpensesNamedRabbitMqVirtualHosts()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepoPath("src/WiSave.Portal/appsettings.json"))
            .Build();

        Assert.Equal("incomes", configuration["RabbitMq:NamedBrokers:Incomes:VirtualHost"]);
        Assert.Equal("expenses", configuration["RabbitMq:NamedBrokers:Expenses:VirtualHost"]);
    }

    private static string RepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WiSave.Portal.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not locate WiSave.Portal repository root.");

        return Path.Combine(directory.FullName, relativePath);
    }
}
