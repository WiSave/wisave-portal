namespace WiSave.Portal.Hubs.Realtime;

public static class RealtimeEventType
{
    public const string FundingAccountOpened = "fundingAccount.opened";
    public const string FundingAccountUpdated = "fundingAccount.updated";
    public const string FundingAccountClosed = "fundingAccount.closed";
    public const string FundingPaymentInstrumentAdded = "fundingPaymentInstrument.added";
    public const string FundingPaymentInstrumentUpdated = "fundingPaymentInstrument.updated";
    public const string FundingPaymentInstrumentRemoved = "fundingPaymentInstrument.removed";
    public const string FundingTransferPosted = "fundingTransfer.posted";
    public const string CreditCardAccountOpened = "creditCardAccount.opened";
    public const string CreditCardAccountUpdated = "creditCardAccount.updated";
    public const string CreditCardAccountClosed = "creditCardAccount.closed";
    public const string CreditCardStateSeeded = "creditCard.stateSeeded";
    public const string CreditCardStatementIssued = "creditCardStatement.issued";
    public const string CreditCardStatementPaymentApplied = "creditCardStatement.paymentApplied";
    public const string ExpenseRecorded = "expense.recorded";
    public const string ExpenseUpdated = "expense.updated";
    public const string ExpenseDeleted = "expense.deleted";
    public const string BudgetCreated = "budget.created";
    public const string BudgetCopiedFromPrevious = "budget.copiedFromPrevious";
    public const string OverallLimitSet = "budget.overallLimitSet";
    public const string CategoryLimitSet = "budget.categoryLimitSet";
    public const string CategoryLimitRemoved = "budget.categoryLimitRemoved";
    public const string CommandFailed = "command.failed";
}
