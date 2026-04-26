using System.Net;
using System.Net.Http.Json;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public sealed class ExpensesRealtimePayloadProvider : IExpensesRealtimePayloadProvider
{
    private static readonly TimeSpan[] ProjectionRetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
    ];

    private readonly HttpClient _httpClient;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delayAsync;

    public ExpensesRealtimePayloadProvider(HttpClient httpClient)
        : this(httpClient, DelayAsync)
    {
    }

    public ExpensesRealtimePayloadProvider(
        HttpClient httpClient,
        Func<TimeSpan, CancellationToken, ValueTask> delayAsync)
    {
        _httpClient = httpClient;
        _delayAsync = delayAsync;
    }

    public async Task<FundingAccountPayload> GetFundingAccountAsync(
        string userId,
        string fundingAccountId,
        CancellationToken ct = default)
    {
        var snapshot = await GetAsync<FundingAccountSnapshotResponse>(
            userId,
            $"/expenses/funding-accounts/{Uri.EscapeDataString(fundingAccountId)}",
            $"Funding account '{fundingAccountId}' was not found in expenses projections.",
            ct);

        return new FundingAccountPayload(
            FundingAccountId: snapshot.Id,
            UserId: snapshot.UserId,
            Name: snapshot.Name,
            Kind: snapshot.Kind,
            Currency: snapshot.Currency,
            Balance: snapshot.Balance,
            Color: snapshot.Color,
            Timestamp: snapshot.UpdatedAt ?? snapshot.CreatedAt);
    }

    public async Task<IReadOnlyList<FundingPaymentInstrumentPayload>> GetFundingPaymentInstrumentsAsync(
        string userId,
        string fundingAccountId,
        CancellationToken ct = default)
    {
        var snapshots = await GetAsync<IReadOnlyList<FundingPaymentInstrumentSnapshotResponse>>(
            userId,
            $"/expenses/funding-accounts/{Uri.EscapeDataString(fundingAccountId)}/payment-instruments",
            $"Funding payment instruments for account '{fundingAccountId}' were not found in expenses projections.",
            ct);

        return snapshots
            .Select(snapshot => new FundingPaymentInstrumentPayload(
                PaymentInstrumentId: snapshot.Id,
                FundingAccountId: snapshot.FundingAccountId,
                UserId: snapshot.UserId,
                Name: snapshot.Name,
                Kind: snapshot.Kind,
                LastFourDigits: snapshot.LastFourDigits,
                Network: snapshot.Network,
                Color: snapshot.Color,
                Timestamp: snapshot.UpdatedAt ?? snapshot.CreatedAt))
            .ToArray();
    }

    private async Task<T> GetAsync<T>(
        string userId,
        string path,
        string notFoundMessage,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("X-User-Id", userId);
            request.Headers.TryAddWithoutValidation("X-User-Permissions", PortalPermissions.Expenses.Read);

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (attempt >= ProjectionRetryDelays.Length)
                    throw new InvalidOperationException(notFoundMessage);

                await _delayAsync(ProjectionRetryDelays[attempt], ct);
                continue;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Expenses response body was empty.");
        }
    }

    private static async ValueTask DelayAsync(TimeSpan delay, CancellationToken ct) =>
        await Task.Delay(delay, ct);

    private sealed record FundingAccountSnapshotResponse(
        string Id,
        string UserId,
        string Name,
        string Kind,
        string Currency,
        decimal Balance,
        string? Color,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record FundingPaymentInstrumentSnapshotResponse(
        string Id,
        string FundingAccountId,
        string UserId,
        string Name,
        string Kind,
        string? LastFourDigits,
        string? Network,
        string? Color,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

}
