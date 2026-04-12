using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WiSave.Portal.Session;
using Xunit;

namespace WiSave.Portal.Tests.Session;

public class SessionConfigurationTests
{
    [Fact]
    public void AddPortalSession_WithoutRedisAndWithoutExplicitFallback_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "false",
                ["Redis:ConnectionString"] = "",
                ["Session:AllowInMemoryTicketStoreFallback"] = "false",
            })
            .Build();

        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddPortalSession(configuration));

        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("Redis:ConnectionString", invalidOperationException.Message);
    }

    [Fact]
    public void AddPortalSession_WithExplicitFallback_RegistersTicketStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "",
                ["Session:AllowInMemoryTicketStoreFallback"] = "true",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddPortalSession(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<ITicketStore>());
        Assert.NotNull(provider.GetRequiredService<IConfigureOptions<CookieAuthenticationOptions>>());
    }

    [Fact]
    public void AddPortalSession_WithRedis_RegistersSharedDataProtectionServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "localhost:6379",
            })
            .Build();

        var services = new ServiceCollection();

        services.AddPortalSession(configuration);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDataProtectionProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType.FullName == "StackExchange.Redis.IConnectionMultiplexer");
    }
}
