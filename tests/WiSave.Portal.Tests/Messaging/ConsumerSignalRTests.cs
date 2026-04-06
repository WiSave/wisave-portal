using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Accounts;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Expenses.Contracts.Models;
using WiSave.Portal.Auth.Models;
using Xunit;

namespace WiSave.Portal.Tests.Messaging;

public class ConsumerSignalRTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public ConsumerSignalRTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", "ConsumerTests_" + Guid.NewGuid());
            builder.UseSetting("Redis:ConnectionString", "");
            builder.ConfigureServices(services =>
            {
                services.AddMassTransitTestHarness();
            });
        });
    }

    public async ValueTask InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "superadmin", "admin", "user" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Database.PortalDbContext>();
        if (!await db.Plans.AnyAsync())
        {
            db.Plans.AddRange(
                new Plan { Id = "free", Name = "Free" },
                new Plan { Id = "standard", Name = "Standard" },
                new Plan { Id = "premium", Name = "Premium" }
            );
            await db.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ExpenseRecorded_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("expense@example.com");

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("ExpenseRecorded", message =>
        {
            tcs.TrySetResult(message);
        });

        await connection.StartAsync(CancellationToken);

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        await harness.Bus.Publish(new ExpenseRecorded(
            ExpenseId: "exp-1",
            UserId: userId,
            AccountId: "acc-1",
            CategoryId: "cat-1",
            SubcategoryId: null,
            Amount: 99.99m,
            Currency: Currency.PLN,
            Date: new DateOnly(2026, 4, 1),
            Description: "Test expense",
            Recurring: false,
            Metadata: null,
            Timestamp: DateTimeOffset.UtcNow
        ), CancellationToken);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("exp-1", result.GetProperty("expenseId").GetString());
        Assert.Equal(userId, result.GetProperty("userId").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task CommandFailed_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("cmdfail@example.com");

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("CommandFailed", message =>
        {
            tcs.TrySetResult(message);
        });

        await connection.StartAsync(CancellationToken);

        var correlationId = Guid.NewGuid();
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        await harness.Bus.Publish(new CommandFailed(
            CorrelationId: correlationId,
            UserId: userId,
            CommandType: "RecordExpense",
            Reason: "Insufficient funds",
            Timestamp: DateTimeOffset.UtcNow
        ), CancellationToken);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("RecordExpense", result.GetProperty("commandType").GetString());
        Assert.Equal("Insufficient funds", result.GetProperty("reason").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task AccountOpened_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("account@example.com");

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("AccountOpened", message =>
        {
            tcs.TrySetResult(message);
        });

        await connection.StartAsync(CancellationToken);

        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        await harness.Bus.Publish(new AccountOpened(
            AccountId: "acc-42",
            UserId: userId,
            Name: "Main Account",
            Type: AccountType.BankAccount,
            Currency: Currency.PLN,
            Balance: 1000m,
            LinkedBankAccountId: null,
            CreditLimit: null,
            BillingCycleDay: null,
            Color: "#FF0000",
            LastFourDigits: "1234",
            Timestamp: DateTimeOffset.UtcNow
        ), CancellationToken);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("acc-42", result.GetProperty("accountId").GetString());
        Assert.Equal("Main Account", result.GetProperty("name").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    private async Task<(HubConnection Connection, string UserId)> CreateAuthenticatedHubConnection(string email)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var request = new RegisterRequest("Test User", email, "Password123!", "free");
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(CancellationToken);
        var userId = auth!.User.Id;

        var cookies = registerResponse.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToList();

        var cookieHeader = string.Join("; ",
            cookies.Select(c => c.Split(';')[0]));

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/notifications", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Headers.Add("Cookie", cookieHeader);
            })
            .Build();

        return (connection, userId);
    }
}
