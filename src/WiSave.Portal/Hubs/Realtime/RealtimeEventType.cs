namespace WiSave.Portal.Hubs.Realtime;

public static class RealtimeEventType
{
    public const string AccountOpened = "account.opened";
    public const string AccountUpdated = "account.updated";
    public const string AccountClosed = "account.closed";
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
