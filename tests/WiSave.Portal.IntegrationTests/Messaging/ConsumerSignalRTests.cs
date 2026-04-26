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
using WiSave.Expenses.Contracts.Events.CreditCards;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Expenses.Contracts.Events.FundingAccounts;
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
    private readonly StubExpensesRealtimePayloadProvider _payloadProvider = new();
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
                services.RemoveAll<IExpensesRealtimePayloadProvider>();
                services.AddSingleton<IExpensesRealtimePayloadProvider>(_payloadProvider);
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
            CommandType: "PostFundingTransfer",
            Reason: "Insufficient funds",
            Timestamp: DateTimeOffset.UtcNow
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("command.failed", envelope.GetProperty("eventType").GetString());
        Assert.True(envelope.GetProperty("entityId").ValueKind == JsonValueKind.Null);
        var payload = envelope.GetProperty("payload");
        Assert.Equal("PostFundingTransfer", payload.GetProperty("commandType").GetString());
        Assert.Equal("Insufficient funds", payload.GetProperty("reason").GetString());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task FundingAccountOpened_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("account@example.com");
        _payloadProvider.Set(new FundingAccountPayload(
            FundingAccountId: "funding-42",
            UserId: userId,
            Name: "Main funding account",
            Kind: "BankAccount",
            Currency: "PLN",
            Balance: 1000m,
            Color: "#FF0000",
            Timestamp: DateTimeOffset.UtcNow));

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "fundingAccount.opened")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        await PublishOnExpensesBus(new FundingAccountOpened(
            FundingAccountId: "funding-42",
            UserId: userId,
            Name: "Main funding account",
            Kind: FundingAccountKind.BankAccount,
            Currency: Currency.PLN,
            OpeningBalance: 1000m,
            Color: "#FF0000",
            Timestamp: DateTimeOffset.UtcNow
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("fundingAccount.opened", envelope.GetProperty("eventType").GetString());
        Assert.Equal("funding-42", envelope.GetProperty("entityId").GetString());
        var payload = envelope.GetProperty("payload");
        Assert.Equal("funding-42", payload.GetProperty("fundingAccountId").GetString());
        Assert.Equal("Main funding account", payload.GetProperty("name").GetString());
        Assert.Equal("BankAccount", payload.GetProperty("kind").GetString());
        Assert.Equal(1000m, payload.GetProperty("balance").GetDecimal());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task CreditCardAccountUpdated_IsPushedToSignalRClient_AsFullSnapshot()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("account-update@example.com");
        _payloadProvider.Set(new CreditCardAccountPayload(
            CreditCardAccountId: "card-77",
            UserId: userId,
            Name: "Credit card account",
            Currency: "PLN",
            SettlementAccountId: "funding-42",
            BankProvider: "MBank",
            ProductCode: "visa-gold",
            CreditLimit: 5000m,
            StatementClosingDay: 16,
            GracePeriodDays: 24,
            UnbilledBalance: 250m,
            ActiveStatementBalance: 1200m,
            ActiveStatementOutstandingBalance: 900m,
            ActiveStatementMinimumPaymentDue: 60m,
            ActiveStatementDueDate: new DateOnly(2026, 5, 10),
            ActiveStatementPeriodCloseDate: new DateOnly(2026, 4, 16),
            Color: "#f59e0b",
            LastFourDigits: "8812",
            Timestamp: DateTimeOffset.UtcNow));

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "creditCardAccount.updated")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        await PublishOnExpensesBus(new CreditCardAccountUpdated(
            CreditCardAccountId: "card-77",
            UserId: userId,
            Name: "Credit card account",
            Currency: Currency.PLN,
            SettlementAccountId: "funding-42",
            BankProvider: BankProvider.MBank,
            ProductCode: "visa-gold",
            CreditLimit: 5000m,
            StatementClosingDay: 16,
            GracePeriodDays: 24,
            Color: "#f59e0b",
            LastFourDigits: "8812",
            Timestamp: DateTimeOffset.UtcNow
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("creditCardAccount.updated", envelope.GetProperty("eventType").GetString());
        Assert.Equal("card-77", envelope.GetProperty("entityId").GetString());
        var payload = envelope.GetProperty("payload");
        Assert.Equal("funding-42", payload.GetProperty("settlementAccountId").GetString());
        Assert.Equal("MBank", payload.GetProperty("bankProvider").GetString());
        Assert.Equal(900m, payload.GetProperty("activeStatementOutstandingBalance").GetDecimal());

        await connection.StopAsync(CancellationToken);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task FundingTransferPosted_IsPushedToSignalRClient()
    {
        var (connection, userId) = await CreateAuthenticatedHubConnection("transfer@example.com");

        var tcs = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("realtimeEvent", envelope =>
        {
            if (envelope.GetProperty("eventType").GetString() == "fundingTransfer.posted")
                tcs.TrySetResult(envelope);
        });

        await connection.StartAsync(CancellationToken);

        await PublishOnExpensesBus(new FundingTransferPosted(
            FundingAccountId: "funding-42",
            UserId: userId,
            TransferId: "transfer-1",
            TargetCreditCardAccountId: "card-77",
            StatementId: "statement-1",
            Amount: 500m,
            PostedAtUtc: DateTimeOffset.UtcNow,
            Timestamp: DateTimeOffset.UtcNow
        ));

        var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
        Assert.Equal("fundingTransfer.posted", envelope.GetProperty("eventType").GetString());
        Assert.Equal("funding-42", envelope.GetProperty("entityId").GetString());
        var payload = envelope.GetProperty("payload");
        Assert.Equal("transfer-1", payload.GetProperty("transferId").GetString());
        Assert.Equal("card-77", payload.GetProperty("targetCreditCardAccountId").GetString());

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

    private sealed class StubExpensesRealtimePayloadProvider : IExpensesRealtimePayloadProvider
    {
        private readonly Dictionary<string, FundingAccountPayload> _fundingAccounts = [];
        private readonly Dictionary<string, CreditCardAccountPayload> _creditCardAccounts = [];

        public void Set(FundingAccountPayload payload) => _fundingAccounts[payload.FundingAccountId] = payload;

        public void Set(CreditCardAccountPayload payload) => _creditCardAccounts[payload.CreditCardAccountId] = payload;

        public Task<FundingAccountPayload> GetFundingAccountAsync(
            string userId,
            string fundingAccountId,
            CancellationToken ct = default)
        {
            if (_fundingAccounts.TryGetValue(fundingAccountId, out var payload))
                return Task.FromResult(payload);

            throw new InvalidOperationException($"Missing test payload for funding account '{fundingAccountId}'.");
        }

        public Task<IReadOnlyList<FundingPaymentInstrumentPayload>> GetFundingPaymentInstrumentsAsync(
            string userId,
            string fundingAccountId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FundingPaymentInstrumentPayload>>([]);

        public Task<CreditCardAccountPayload> GetCreditCardAccountAsync(
            string userId,
            string creditCardAccountId,
            CancellationToken ct = default)
        {
            if (_creditCardAccounts.TryGetValue(creditCardAccountId, out var payload))
                return Task.FromResult(payload);

            throw new InvalidOperationException($"Missing test payload for credit card account '{creditCardAccountId}'.");
        }
    }
}
