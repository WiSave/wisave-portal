namespace WiSave.Portal.Contracts.Authorization;

/// <summary>
/// Defines the permission names shared between the portal and downstream services.
/// </summary>
public static class PortalPermissions
{
    /// <summary>
    /// Defines permission names for the expenses service.
    /// </summary>
    public static class Expenses
    {
        /// <summary>
        /// Grants read access to expenses data.
        /// </summary>
        public const string Read = "expenses:read";

        /// <summary>
        /// Grants create and update access to expenses data.
        /// </summary>
        public const string Write = "expenses:write";

        /// <summary>
        /// Grants delete access to expenses data.
        /// </summary>
        public const string Delete = "expenses:delete";
    }

    /// <summary>
    /// Defines permission names for the incomes service.
    /// </summary>
    public static class Incomes
    {
        /// <summary>
        /// Grants read access to incomes data.
        /// </summary>
        public const string Read = "incomes:read";

        /// <summary>
        /// Grants create and update access to incomes data.
        /// </summary>
        public const string Write = "incomes:write";

        /// <summary>
        /// Grants delete access to incomes data.
        /// </summary>
        public const string Delete = "incomes:delete";

        /// <summary>
        /// Grants access to import incomes in bulk.
        /// </summary>
        public const string Import = "incomes:import";
    }

    /// <summary>
    /// Defines permission names for the stocks service.
    /// </summary>
    public static class Stocks
    {
        /// <summary>
        /// Grants read access to stock data.
        /// </summary>
        public const string Read = "stocks:read";

        /// <summary>
        /// Grants create and update access to stock data.
        /// </summary>
        public const string Write = "stocks:write";

        /// <summary>
        /// Grants access to manage portfolios.
        /// </summary>
        public const string PortfolioManage = "stocks:portfolio:manage";

        /// <summary>
        /// Grants access to manage watchlists.
        /// </summary>
        public const string WatchlistManage = "stocks:watchlist:manage";
    }
}
