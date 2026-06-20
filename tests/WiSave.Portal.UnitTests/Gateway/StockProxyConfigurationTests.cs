using System.Text.Json;
using Xunit;

namespace WiSave.Portal.UnitTests.Gateway;

public sealed class StockProxyConfigurationTests
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
    public void DockerCompose_Portal_UsesSdkPublishedImage()
    {
        var compose = File.ReadAllText(RepoPath("docker-compose.yml"));

        Assert.Contains("image: wisave-portal:latest", compose);
        Assert.Contains("pull_policy: never", compose);
        Assert.DoesNotContain("dockerfile:", compose);
        Assert.DoesNotContain("github_packages_token", compose);
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
