using System.Net;
using System.Net.Http.Json;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public sealed class ExpensesRealtimePayloadProvider(HttpClient httpClient) : IExpensesRealtimePayloadProvider
{
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

    public async Task<CreditCardAccountPayload> GetCreditCardAccountAsync(
        string userId,
        string creditCardAccountId,
        CancellationToken ct = default)
    {
        var snapshot = await GetAsync<CreditCardAccountSnapshotResponse>(
            userId,
            $"/expenses/credit-cards/{Uri.EscapeDataString(creditCardAccountId)}",
            $"Credit card account '{creditCardAccountId}' was not found in expenses projections.",
            ct);

        return new CreditCardAccountPayload(
            CreditCardAccountId: snapshot.Id,
            UserId: snapshot.UserId,
            Name: snapshot.Name,
            Currency: snapshot.Currency,
            SettlementAccountId: snapshot.SettlementAccountId,
            BankProvider: snapshot.BankProvider,
            ProductCode: snapshot.ProductCode,
            CreditLimit: snapshot.CreditLimit,
            StatementClosingDay: snapshot.StatementClosingDay,
            GracePeriodDays: snapshot.GracePeriodDays,
            UnbilledBalance: snapshot.UnbilledBalance,
            ActiveStatementBalance: snapshot.ActiveStatementBalance,
            ActiveStatementOutstandingBalance: snapshot.ActiveStatementOutstandingBalance,
            ActiveStatementMinimumPaymentDue: snapshot.ActiveStatementMinimumPaymentDue,
            ActiveStatementDueDate: snapshot.ActiveStatementDueDate,
            ActiveStatementPeriodCloseDate: snapshot.ActiveStatementPeriodCloseDate,
            Color: snapshot.Color,
            LastFourDigits: snapshot.LastFourDigits,
            Timestamp: snapshot.UpdatedAt ?? snapshot.CreatedAt);
    }

    private async Task<T> GetAsync<T>(
        string userId,
        string path,
        string notFoundMessage,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-User-Id", userId);
        request.Headers.TryAddWithoutValidation("X-User-Permissions", PortalPermissions.Expenses.Read);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException(notFoundMessage);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Expenses response body was empty.");
    }

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

    private sealed record CreditCardAccountSnapshotResponse(
        string Id,
        string UserId,
        string Name,
        string Currency,
        string SettlementAccountId,
        string BankProvider,
        string ProductCode,
        decimal CreditLimit,
        int StatementClosingDay,
        int GracePeriodDays,
        decimal UnbilledBalance,
        decimal? ActiveStatementBalance,
        decimal? ActiveStatementOutstandingBalance,
        decimal? ActiveStatementMinimumPaymentDue,
        DateOnly? ActiveStatementDueDate,
        DateOnly? ActiveStatementPeriodCloseDate,
        string? Color,
        string? LastFourDigits,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);
}
