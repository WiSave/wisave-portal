namespace WiSave.Portal.Migrations;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var connectionString = args.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Portal");
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine(
                    "Connection string not provided. Pass it as the first argument or set ConnectionStrings__Portal.");
                return 1;
            }

            DbMigrator.Run(connectionString);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error when running migration: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
