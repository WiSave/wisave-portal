using Microsoft.AspNetCore.Authorization;

namespace WiSave.Portal.Authorization;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddPermissionPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy("require-expenses", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new PermissionRequirement("expenses"));
            })
            .AddPolicy("require-incomes", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new PermissionRequirement("incomes"));
            })
            .AddPolicy("require-stocks", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new PermissionRequirement("stocks"));
            });

        return services;
    }
}
