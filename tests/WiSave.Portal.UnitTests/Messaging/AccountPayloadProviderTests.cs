using System.Net;
using System.Net.Http;
using System.Text;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Hubs.Realtime;
using WiSave.Portal.Messaging;
using Xunit;

namespace WiSave.Portal.UnitTests.Messaging;

public class AccountPayloadProviderTests
{
    [Fact]
    public async Task GetAsync_fetches_account_snapshot_and_maps_credit_card_fields()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": "card-1",
                      "userId": "user-1",
                      "name": "Millennium",
                      "type": "CreditCard",
                      "variant": null,
                      "currency": "PLN",
                      "balance": null,
                      "creditLimit": 5000,
                      "billingCycleDay": 16,
                      "previousCycleDebt": 1200,
                      "currentCycleDebt": 340,
                      "linkedBankAccountId": "bank-1",
                      "color": "#f59e0b",
                      "lastFourDigits": "4532",
                      "isActive": true,
                      "createdAt": "2026-04-19T10:00:00Z",
                      "updatedAt": "2026-04-19T11:00:00Z"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://expenses.local"),
        };

        var provider = new AccountPayloadProvider(httpClient);

        var payload = await provider.GetAsync("user-1", "card-1", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        Assert.Equal(new Uri("http://expenses.local/expenses/accounts/card-1"), capturedRequest.RequestUri);
        Assert.Equal("user-1", capturedRequest.Headers.GetValues("X-User-Id").Single());
        Assert.Equal(PortalPermissions.Expenses.Read, capturedRequest.Headers.GetValues("X-User-Permissions").Single());

        Assert.Equal("card-1", payload.AccountId);
        Assert.Equal("CreditCard", payload.Type);
        Assert.Null(payload.Variant);
        Assert.Equal(1200m, payload.PreviousCycleDebt);
        Assert.Equal(340m, payload.CurrentCycleDebt);
        Assert.Equal("bank-1", payload.LinkedBankAccountId);
        Assert.Equal(DateTimeOffset.Parse("2026-04-19T11:00:00Z"), payload.Timestamp);
    }

    [Fact]
    public async Task GetAsync_normalizes_debit_card_variant_to_lowercase()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": "card-2",
                      "userId": "user-2",
                      "name": "Travel Card",
                      "type": "DebitCard",
                      "variant": "Standalone",
                      "currency": "EUR",
                      "balance": 250,
                      "creditLimit": null,
                      "billingCycleDay": null,
                      "previousCycleDebt": null,
                      "currentCycleDebt": null,
                      "linkedBankAccountId": null,
                      "color": null,
                      "lastFourDigits": "8812",
                      "isActive": true,
                      "createdAt": "2026-04-19T10:00:00Z",
                      "updatedAt": null
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://expenses.local"),
        };

        var provider = new AccountPayloadProvider(httpClient);

        var payload = await provider.GetAsync("user-2", "card-2", CancellationToken.None);

        Assert.Equal("standalone", payload.Variant);
        Assert.Equal(250m, payload.Balance);
        Assert.Equal(DateTimeOffset.Parse("2026-04-19T10:00:00Z"), payload.Timestamp);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
