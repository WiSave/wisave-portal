namespace WiSave.Portal.Hubs.Realtime;

public sealed record FundingAccountPayload(
    string FundingAccountId,
    string UserId,
    string Name,
    string Kind,
    string Currency,
    decimal Balance,
    string? Color,
    DateTimeOffset Timestamp);

public sealed record FundingPaymentInstrumentPayload(
    string PaymentInstrumentId,
    string FundingAccountId,
    string UserId,
    string Name,
    string Kind,
    string? LastFourDigits,
    string? Network,
    string? Color,
    DateTimeOffset Timestamp);
