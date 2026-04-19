using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Hubs;
using Xunit;

namespace WiSave.Portal.UnitTests.Hubs;

public class SignalRBackplaneRegistrationTests
{
    [Fact]
    public void AddPortalSignalR_runs_without_backplane_when_connection_string_absent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var ex = Record.Exception(() => services.AddPortalSignalR(config));

        Assert.Null(ex);
    }

    [Fact]
    public void AddPortalSignalR_accepts_Redis_connection_string_without_error()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Redis:ConnectionString"] = "localhost:6379" })
            .Build();

        var ex = Record.Exception(() => services.AddPortalSignalR(config));

        Assert.Null(ex);
    }
}
