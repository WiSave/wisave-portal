namespace WiSave.Portal.Hubs.Realtime;

public static class RealtimeEventType
{
    public const string ExpenseCreated = "expense.created";
    public const string CategoryCreated = "category.created";
    public const string ExpenseRecorded = "expense.recorded";
    public const string CommandFailed = "command.failed";
}
