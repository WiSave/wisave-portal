using System.Text.Json;
using Xunit;

namespace WiSave.Portal.UnitTests.Gateway;

public sealed class ProxyConfigurationTests
{
    [Fact]
    public void Appsettings_StocksCluster_TargetsCurrentLocalStockPort()
    {
        var appsettings = JsonDocument.Parse(File.ReadAllText(RepoPath("src/WiSave.Portal/appsettings.json")));

        var address = appsettings.RootElement
            .GetProperty("ReverseProxy")
            .GetProperty("Clusters")
            .GetProperty("stocks-cluster")
            .GetProperty("Destinations")
            .GetProperty("destination1")
            .GetProperty("Address")
            .GetString();

        Assert.Equal("http://localhost:5300", address);
    }

    [Fact]
    public void DockerCompose_StocksCluster_TargetsStockWebApiService()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.yml"));

        Assert.Contains(
            "ReverseProxy__Clusters__stocks-cluster__Destinations__destination1__Address=http://wisave-stock-webapi:8080",
            compose);
        Assert.DoesNotContain(
            "ReverseProxy__Clusters__stocks-cluster__Destinations__destination1__Address=http://wisave-stocks:8080",
            compose);
    }

    [Fact]
    public void DockerCompose_IncomesCluster_TargetsIncomesWebApiService()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.yml"));

        Assert.Contains(
            "ReverseProxy__Clusters__incomes-cluster__Destinations__destination1__Address=http://wisave-incomes-webapi:8080",
            compose);
        Assert.DoesNotContain(
            "ReverseProxy__Clusters__incomes-cluster__Destinations__destination1__Address=http://wisave-incomes:8080",
            compose);
    }

    [Fact]
    public void DockerCompose_Portal_UsesDockerfileBuildForRiderDebugging()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.yml"));
        var dockerfilePath = RepoPath("src/WiSave.Portal/Dockerfile");

        Assert.True(File.Exists(dockerfilePath));
        Assert.Contains("build:", compose);
        Assert.Contains("dockerfile: src/WiSave.Portal/Dockerfile", compose);
        Assert.Contains("wisave_expenses_contracts_package: ${HOME}/.nuget/packages/wisave.expenses.contracts", compose);
        Assert.Contains("wisave_incomes_contracts_package: ${HOME}/.nuget/packages/wisave.incomes.contracts", compose);
        Assert.Contains("github_packages_token", compose);
        Assert.DoesNotContain("pull_policy: never", compose);
    }

    [Fact]
    public void DockerCompose_Portal_UsesPortalRabbitMqVirtualHost()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.yml"));

        Assert.Contains("RabbitMq__VirtualHost=portal", compose);
        Assert.DoesNotContain("RabbitMq__VirtualHost=expenses", compose);
    }

    [Fact]
    public void RabbitMqDefinitions_CreatePortalVirtualHost()
    {
        using var definitions = JsonDocument.Parse(File.ReadAllText(RepoPath("infrastructure/rabbitmq/definitions.json")));

        var vhosts = definitions.RootElement
            .GetProperty("vhosts")
            .EnumerateArray()
            .Select(vhost => vhost.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("portal", vhosts);

        var permissions = definitions.RootElement
            .GetProperty("permissions")
            .EnumerateArray()
            .Where(permission => permission.GetProperty("user").GetString() == "guest")
            .Select(permission => permission.GetProperty("vhost").GetString())
            .ToArray();

        Assert.Contains("portal", permissions);
    }

    [Fact]
    public void DockerCompose_Portal_IsProfileGatedForLocalDebugging()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.yml"));

        Assert.Contains(
            """
              portal:
                profiles:
                  - portal
                build:
                  context: .
                  dockerfile: src/WiSave.Portal/Dockerfile
            """,
            compose);
    }

    private static string RepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WiSave.Portal.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate WiSave.Portal repository root.");
        }

        return Path.Combine(directory.FullName, relativePath);
    }
}
