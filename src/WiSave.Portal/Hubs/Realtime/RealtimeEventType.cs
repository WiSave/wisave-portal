namespace WiSave.Portal.Hubs.Realtime;

public static class RealtimeEventType
{
    public const string ExpenseCreated = "expense.created";
    public const string CategoryCreated = "category.created";
    public const string CategoryUpdated = "category.updated";
    public const string CategoryDeleted = "category.deleted";
    public const string SubcategoryCreated = "subcategory.created";
    public const string SubcategoryUpdated = "subcategory.updated";
    public const string SubcategoryDeleted = "subcategory.deleted";
    public const string ExpenseRecorded = "expense.recorded";
    public const string CommandFailed = "command.failed";
}
