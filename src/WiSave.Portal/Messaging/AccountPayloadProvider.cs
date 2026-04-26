using System.Net;
using System.Net.Http.Json;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public sealed class AccountPayloadProvider(HttpClient httpClient) : IAccountPayloadProvider
{
    public async Task<AccountPayload> GetAsync(string userId, string accountId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/expenses/accounts/{Uri.EscapeDataString(accountId)}");
        request.Headers.TryAddWithoutValidation("X-User-Id", userId);
        request.Headers.TryAddWithoutValidation("X-User-Permissions", PortalPermissions.Expenses.Read);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Account '{accountId}' was not found in expenses projections.");

        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<AccountSnapshotResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Expenses account response body was empty.");

        return new AccountPayload(
            AccountId: snapshot.Id,
            UserId: snapshot.UserId,
            Name: snapshot.Name,
            Type: snapshot.Type,
            Variant: NormalizeVariant(snapshot.Variant),
            Currency: snapshot.Currency,
            Balance: snapshot.Balance,
            LinkedBankAccountId: snapshot.LinkedBankAccountId,
            CreditLimit: snapshot.CreditLimit,
            BillingCycleDay: snapshot.BillingCycleDay,
            PreviousCycleDebt: snapshot.PreviousCycleDebt,
            CurrentCycleDebt: snapshot.CurrentCycleDebt,
            Color: snapshot.Color,
            LastFourDigits: snapshot.LastFourDigits,
            Timestamp: snapshot.UpdatedAt ?? snapshot.CreatedAt);
    }

    private static string? NormalizeVariant(string? variant) =>
        variant switch
        {
            "Linked" or "linked" => "linked",
            "Standalone" or "standalone" => "standalone",
            _ => null,
        };

    private sealed record AccountSnapshotResponse(
        string Id,
        string UserId,
        string Name,
        string Type,
        string? Variant,
        string Currency,
        decimal? Balance,
        decimal? CreditLimit,
        int? BillingCycleDay,
        decimal? PreviousCycleDebt,
        decimal? CurrentCycleDebt,
        string? LinkedBankAccountId,
        string? Color,
        string? LastFourDigits,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);
}
