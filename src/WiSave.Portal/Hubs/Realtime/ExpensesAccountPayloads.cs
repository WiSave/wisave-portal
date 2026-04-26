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

public sealed record CreditCardAccountPayload(
    string CreditCardAccountId,
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
    DateTimeOffset Timestamp);
