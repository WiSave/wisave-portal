using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Authorization;
using WiSave.Portal.Infrastructure.Database;

namespace WiSave.Portal.Endpoints;

internal sealed class AntiforgeryValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Antiforgery token validation failed.");
        }
        return await next(context);
    }
}

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth");

        group.MapPost("/register", Register)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .RequireRateLimiting("auth-register")
            .Produces<AuthResponse>()
            .ProducesValidationProblem()
            .WithSummary("Register a new user account");

        group.MapPost("/login", Login)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .RequireRateLimiting("auth-login")
            .Produces<AuthResponse>()
            .Produces(401)
            .WithSummary("Authenticate with email and password");

        group.MapPost("/logout", Logout)
            .RequireAuthorization()
            .Produces(204)
            .WithSummary("Clear session");

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .Produces<UserResponse>()
            .Produces(401)
            .WithSummary("Get current user from session");

        group.MapGet("/antiforgery-token", (IAntiforgery antiforgery, HttpContext context) =>
        {
            SetXsrfTokenCookie(antiforgery, context);
            return Results.Ok();
        })
            .AllowAnonymous()
            .Produces(200)
            .WithSummary("Get XSRF token cookie and request token");
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAntiforgery antiforgery,
        HttpContext context,
        PortalDbContext db,
        UserPlanCache userPlanCache,
        PlanPermissionCache planPermissionCache)
    {
        var planId = string.IsNullOrWhiteSpace(request.PlanId) ? "free" : request.PlanId;
        var planExists = await db.Plans.AnyAsync(p => p.Id == planId && p.IsActive);
        if (!planExists)
        {
            return Results.BadRequest(new { errors = new[] { $"Plan '{planId}' does not exist." } });
        }

        var user = new ApplicationUser
        {
            Name = request.Name,
            Email = request.Email,
            UserName = request.Email,
            PlanId = planId
        };

        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        await userManager.AddToRoleAsync(user, "user");

        await signInManager.SignInAsync(user, isPersistent: true);

        SetXsrfTokenCookie(antiforgery, context);

        var permissions = await ResolvePermissionsAsync(user.Id, userManager, userPlanCache, planPermissionCache);
        var response = new AuthResponse(new UserResponse(user.Id, user.Name, user.Email, permissions));
        return Results.Ok(response);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAntiforgery antiforgery,
        HttpContext context,
        UserPlanCache userPlanCache,
        PlanPermissionCache planPermissionCache)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut || !result.Succeeded)
        {
            return Results.Unauthorized();
        }

        SetXsrfTokenCookie(antiforgery, context);

        var permissions = await ResolvePermissionsAsync(user.Id, userManager, userPlanCache, planPermissionCache);
        var response = new AuthResponse(new UserResponse(user.Id, user.Name, user.Email, permissions));
        return Results.Ok(response);
    }

    private static async Task<IResult> Logout(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        UserPlanCache userPlanCache,
        PlanPermissionCache planPermissionCache)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var permissions = await ResolvePermissionsAsync(user.Id, userManager, userPlanCache, planPermissionCache);
        var response = new UserResponse(user.Id, user.Name, user.Email!, permissions);
        return Results.Ok(response);
    }

    /// <summary>
    /// Calls GetAndStoreTokens (sets the HttpOnly antiforgery cookie) and then
    /// writes a separate non-HttpOnly XSRF-TOKEN cookie containing the request token.
    /// Angular's withXsrfConfiguration reads this cookie and sends its value as
    /// the X-XSRF-TOKEN header on subsequent requests.
    /// </summary>
    private static void SetXsrfTokenCookie(IAntiforgery antiforgery, HttpContext context)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
    }

    private static async Task<string[]> ResolvePermissionsAsync(
        string userId,
        UserManager<ApplicationUser> userManager,
        UserPlanCache userPlanCache,
        PlanPermissionCache planPermissionCache)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return [];

        var roles = await userManager.GetRolesAsync(user);
        if (roles.Any(r => r is "superadmin" or "admin"))
            return ["*"];

        var planId = await userPlanCache.GetPlanIdAsync(userId);
        if (planId is null) return [];

        var permissions = await planPermissionCache.GetPermissionsAsync(planId);
        return [.. permissions];
    }
}
