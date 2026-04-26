using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public interface IExpensesRealtimePayloadProvider
{
    Task<FundingAccountPayload> GetFundingAccountAsync(
        string userId,
        string fundingAccountId,
        CancellationToken ct = default);

    Task<IReadOnlyList<FundingPaymentInstrumentPayload>> GetFundingPaymentInstrumentsAsync(
        string userId,
        string fundingAccountId,
        CancellationToken ct = default);

    Task<CreditCardAccountPayload> GetCreditCardAccountAsync(
        string userId,
        string creditCardAccountId,
        CancellationToken ct = default);
}
