namespace WiSave.Portal.Hubs.Realtime;

public sealed record AccountPayload(
    string AccountId,
    string UserId,
    string Name,
    string Type,
    string? Variant,
    string Currency,
    decimal? Balance,
    string? LinkedBankAccountId,
    decimal? CreditLimit,
    int? BillingCycleDay,
    decimal? PreviousCycleDebt,
    decimal? CurrentCycleDebt,
    string? Color,
    string? LastFourDigits,
    DateTimeOffset Timestamp);
