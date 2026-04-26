using System.Net;
using System.Text;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Messaging;
using Xunit;

namespace WiSave.Portal.UnitTests.Messaging;

public class ExpensesRealtimePayloadProviderTests
{
    [Fact]
    public async Task GetFundingAccountAsync_fetches_funding_account_snapshot()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse(
                """
                {
                  "id": "funding-1",
                  "userId": "user-1",
                  "name": "Main bank",
                  "kind": "BankAccount",
                  "currency": "PLN",
                  "balance": 1200.50,
                  "color": "#2563eb",
                  "isActive": true,
                  "createdAt": "2026-04-26T10:00:00Z",
                  "updatedAt": "2026-04-26T11:00:00Z"
                }
                """);
        });

        using var httpClient = CreateClient(handler);
        var provider = new ExpensesRealtimePayloadProvider(httpClient);

        var payload = await provider.GetFundingAccountAsync("user-1", "funding-1", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        Assert.Equal(new Uri("http://expenses.local/expenses/funding-accounts/funding-1"), capturedRequest.RequestUri);
        Assert.Equal("user-1", capturedRequest.Headers.GetValues("X-User-Id").Single());
        Assert.Equal(PortalPermissions.Expenses.Read, capturedRequest.Headers.GetValues("X-User-Permissions").Single());

        Assert.Equal("funding-1", payload.FundingAccountId);
        Assert.Equal("BankAccount", payload.Kind);
        Assert.Equal(1200.50m, payload.Balance);
        Assert.Equal(DateTimeOffset.Parse("2026-04-26T11:00:00Z"), payload.Timestamp);
    }

    [Fact]
    public async Task GetFundingAccountAsync_retries_not_found_projection_lag()
    {
        var attempts = 0;

        var handler = new StubHttpMessageHandler(_ =>
        {
            attempts++;
            if (attempts < 3)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return JsonResponse(
                """
                {
                  "id": "funding-1",
                  "userId": "user-1",
                  "name": "Main bank",
                  "kind": "BankAccount",
                  "currency": "PLN",
                  "balance": 1200.50,
                  "color": "#2563eb",
                  "isActive": true,
                  "createdAt": "2026-04-26T10:00:00Z",
                  "updatedAt": "2026-04-26T11:00:00Z"
                }
                """);
        });

        using var httpClient = CreateClient(handler);
        var provider = new ExpensesRealtimePayloadProvider(
            httpClient,
            static (_, _) => ValueTask.CompletedTask);

        var payload = await provider.GetFundingAccountAsync("user-1", "funding-1", CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal("funding-1", payload.FundingAccountId);
    }

    [Fact]
    public async Task GetFundingPaymentInstrumentsAsync_fetches_payment_instruments()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return JsonResponse(
                """
                [
                  {
                    "id": "pi-1",
                    "fundingAccountId": "funding-1",
                    "userId": "user-1",
                    "name": "Debit card",
                    "kind": "DebitCard",
                    "lastFourDigits": "1234",
                    "network": "Visa",
                    "color": "#16a34a",
                    "isActive": true,
                    "createdAt": "2026-04-26T10:00:00Z",
                    "updatedAt": null
                  }
                ]
                """);
        });

        using var httpClient = CreateClient(handler);
        var provider = new ExpensesRealtimePayloadProvider(httpClient);

        var payloads = await provider.GetFundingPaymentInstrumentsAsync("user-1", "funding-1", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        Assert.Equal(new Uri("http://expenses.local/expenses/funding-accounts/funding-1/payment-instruments"), capturedRequest.RequestUri);
        var payload = Assert.Single(payloads);
        Assert.Equal("pi-1", payload.PaymentInstrumentId);
        Assert.Equal("DebitCard", payload.Kind);
        Assert.Equal("1234", payload.LastFourDigits);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://expenses.local") };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
