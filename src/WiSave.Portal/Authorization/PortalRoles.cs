namespace WiSave.Portal.Authorization;

public static class PortalRoles
{
    public const string FreePlan = "plan:free";
    public const string StandardPlan = "plan:standard";
    public const string PremiumPlan = "plan:premium";
    public const string Admin = "admin";
    public const string SuperAdmin = "superadmin";

    public static readonly string[] PlanRoles = [FreePlan, StandardPlan, PremiumPlan];
    public static readonly string[] AdminRoles = [Admin, SuperAdmin];

    public static string NormalizePlanInput(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return FreePlan;

        var trimmed = plan.Trim();
        return trimmed.StartsWith("plan:", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant()
            : $"plan:{trimmed.ToLowerInvariant()}";
    }

    public static bool IsPlanRole(string role) =>
        PlanRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
