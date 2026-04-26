using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WiSave.Expenses.Contracts.Bus;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Accounts;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Expenses.Contracts.Models;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Contracts.Bus;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Hubs.Realtime;
using WiSave.Portal.Messaging;
using Xunit;

namespace WiSave.Portal.IntegrationTests.Messaging;

public class ConsumerSignalRTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubAccountPayloadProvider _accountPayloadProvider = new();
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public ConsumerSignalRTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("InMemoryDatabaseName", "ConsumerTests_" + Guid.NewGuid());
            builder.UseSetting("Messaging:Transport", "InMemory");
            builder.UseSetting("Redis:ConnectionString", "");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountPayloadProvider>();
                services.AddSingleton<IAccountPayloadProvider>(_accountPayloadProvider);
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

        if (!await db.Permissions.AnyAsync())
        {
            var permissions = new[]
            {
                new Permission { Id = Guid.Parse("a3000000-0000-0000-0000-000000000001"), Name = PortalPermissions.Expenses.Read },
                new Permission { Id = Guid.Parse("a3000000-0000-0000-0000-000000000002"), Name = PortalPermissions.Expenses.Write },
                new Permission { Id = Guid.Parse("a3000000-0000-0000-0000-000000000003"), Name = PortalPermissions.Expenses.Delete },
            };
            db.Permissions.AddRange(permissions);
            await db.SaveChangesAsync();

            db.PlanPermissions.AddRange(
                new PlanPermission { PlanId = "free", PermissionId = permissions[0].Id },
                new PlanPermission { PlanId = "standard", PermissionId = permissions[0].Id },
                new PlanPermission { PlanId = "standard", PermissionId = permissions[1].Id },
                new PlanPermission { PlanId = "premium", PermissionId = permissions[0].Id },
                new PlanPermission { PlanId = "premium", PermissionId = permissions[1].Id },
                new PlanPermission { PlanId = "premium", PermissionId = permissions[2].Id }
            );
            await db.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Services_ExposeTypedExpensesBusPublishEndpoint()
    {
        using var scope = _factory.Services.CreateScope();

        var expensesBus = scope.ServiceProvider.GetService<IExpensesBus>();
        var portalBus = scope.ServiceProvider.GetService<IPortalBus>();

        Assert.NotNull(expensesBus);
        Assert.NotNull(portalBus);
    }

    [Fact]
    public async Task ExpenseRecorded_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("expense@example.com");

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "expense.recorded")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        await PublishOnExpensesBus(new ExpenseRecorded(
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
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("expenses", envelope.GetProperty("domain").GetString());
        Assert.Equal("expense.recorded", envelope.GetProperty("eventType").GetString());
        Assert.Equal("exp-1", envelope.GetProperty("entityId").GetString());
        var payload = envelope.GetProperty("payload");
        Assert.Equal("exp-1", payload.GetProperty("expenseId").GetString());
        Assert.Equal(userId, payload.GetProperty("userId").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task CommandFailed_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("cmdfail@example.com");

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "command.failed")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        var correlationId = Guid.NewGuid();
        await PublishOnExpensesBus(new CommandFailed(
            CorrelationId: correlationId,
            UserId: userId,
            CommandType: "RecordExpense",
            Reason: "Insufficient funds",
            Timestamp: DateTimeOffset.UtcNow
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("command.failed", envelope.GetProperty("eventType").GetString());
        Assert.True(envelope.GetProperty("entityId").ValueKind == JsonValueKind.Null);
        var payload = envelope.GetProperty("payload");
        Assert.Equal("RecordExpense", payload.GetProperty("commandType").GetString());
        Assert.Equal("Insufficient funds", payload.GetProperty("reason").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task AccountOpened_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("account@example.com");
        _accountPayloadProvider.Set(new AccountPayload(
            AccountId: "acc-42",
            UserId: userId,
            Name: "Main Account",
            Type: "CreditCard",
            Variant: null,
            Currency: "PLN",
            Balance: null,
            LinkedBankAccountId: "bank-1",
            CreditLimit: 5000m,
            BillingCycleDay: 16,
            PreviousCycleDebt: 1200m,
            CurrentCycleDebt: 340m,
            Color: "#FF0000",
            LastFourDigits: "1234",
            Timestamp: DateTimeOffset.UtcNow));

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "account.opened")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        await PublishOnExpensesBus(new AccountOpened(
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
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("account.opened", envelope.GetProperty("eventType").GetString());
        Assert.Equal("acc-42", envelope.GetProperty("entityId").GetString());
        var payload = envelope.GetProperty("payload");
        Assert.Equal("acc-42", payload.GetProperty("accountId").GetString());
        Assert.Equal("Main Account", payload.GetProperty("name").GetString());
        Assert.Equal("CreditCard", payload.GetProperty("type").GetString());
        Assert.True(payload.GetProperty("variant").ValueKind == JsonValueKind.Null);
        Assert.Equal(1200m, payload.GetProperty("previousCycleDebt").GetDecimal());
        Assert.Equal(340m, payload.GetProperty("currentCycleDebt").GetDecimal());
        Assert.Equal("bank-1", payload.GetProperty("linkedBankAccountId").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task AccountUpdated_IsPushedToSignalRClient_AsFullSnapshot()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("account-update@example.com");
        _accountPayloadProvider.Set(new AccountPayload(
            AccountId: "acc-77",
            UserId: userId,
            Name: "Travel Card",
            Type: "DebitCard",
            Variant: "standalone",
            Currency: "EUR",
            Balance: 250m,
            LinkedBankAccountId: null,
            CreditLimit: null,
            BillingCycleDay: null,
            PreviousCycleDebt: null,
            CurrentCycleDebt: null,
            Color: null,
            LastFourDigits: "8812",
            Timestamp: DateTimeOffset.UtcNow));

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "account.updated")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        await PublishOnExpensesBus(new AccountUpdated(
            AccountId: "acc-77",
            UserId: userId,
            Name: "Travel Card",
            Type: AccountType.DebitCard,
            Currency: Currency.EUR,
            Balance: 250m,
            LinkedBankAccountId: null,
            CreditLimit: null,
            BillingCycleDay: null,
            Color: null,
            LastFourDigits: "8812",
            Timestamp: DateTimeOffset.UtcNow
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("account.updated", envelope.GetProperty("eventType").GetString());
        Assert.Equal("acc-77", envelope.GetProperty("entityId").GetString());
        var payload = envelope.GetProperty("payload");
        Assert.Equal("DebitCard", payload.GetProperty("type").GetString());
        Assert.Equal("standalone", payload.GetProperty("variant").GetString());
        Assert.Equal(250m, payload.GetProperty("balance").GetDecimal());
        Assert.True(payload.GetProperty("linkedBankAccountId").ValueKind == JsonValueKind.Null);

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    private async Task<(HubConnection Connection, string UserId)> CreateAuthenticatedHubConnection(string email)
    {
        var cookieContainer = new System.Net.CookieContainer();
        var handler = new CookieDelegatingHandler(cookieContainer, _factory.Server.CreateHandler());
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };

        var afToken = await GetAntiforgeryTokenAsync(client);
        var request = new RegisterRequest("Test User", email, "Password123!", "free");
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        msg.Headers.Add("X-XSRF-TOKEN", afToken);
        msg.Content = JsonContent.Create(request);
        var registerResponse = await client.SendAsync(msg, CancellationToken);
        var registerBody = await registerResponse.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(
            registerResponse.StatusCode == HttpStatusCode.OK,
            $"Expected 200 from /api/auth/register but got {(int)registerResponse.StatusCode} {registerResponse.StatusCode}. Body: {registerBody}");

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

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);
        response.EnsureSuccessStatusCode();

        var xsrfCookie = response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith("XSRF-TOKEN="));
        return Uri.UnescapeDataString(xsrfCookie.Split('=', 2)[1].Split(';')[0]);
    }

    private async Task PublishOnExpensesBus<T>(T message)
        where T : class
    {
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IExpensesBus>();

        await bus.Publish(message, CancellationToken);
    }

    // Delegating handler that manages a cookie container, forwarding cookies on requests
    // and storing cookies from responses — while leaving Set-Cookie headers visible in the response.
    private sealed class CookieDelegatingHandler(System.Net.CookieContainer cookieContainer, HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cookieHeader = cookieContainer.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            {
                foreach (var setCookie in setCookieHeaders)
                {
                    try { cookieContainer.SetCookies(request.RequestUri!, setCookie); }
                    catch (System.Net.CookieException) { /* ignore malformed cookies */ }
                }
            }

            return response;
        }
    }

    private sealed class StubAccountPayloadProvider : IAccountPayloadProvider
    {
        private readonly Dictionary<string, AccountPayload> _payloads = [];

        public void Set(AccountPayload payload) => _payloads[payload.AccountId] = payload;

        public Task<AccountPayload> GetAsync(string userId, string accountId, CancellationToken ct = default)
        {
            if (_payloads.TryGetValue(accountId, out var payload))
                return Task.FromResult(payload);

            throw new InvalidOperationException($"Missing test payload for account '{accountId}'.");
        }
    }
}
