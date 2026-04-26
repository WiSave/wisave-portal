using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Authorization;
using WiSave.Portal.Filters;

namespace WiSave.Portal.Endpoints;

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
            .Produces<AuthErrorResponse>(401)
            .WithSummary("Authenticate with email and password");

        group.MapPost("/logout", Logout)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .RequireAuthorization()
            .Produces(204)
            .WithSummary("Clear session");

        group.MapPost("/change-password", ChangePassword)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .RequireAuthorization()
            .Produces(204)
            .ProducesValidationProblem()
            .Produces(401)
            .WithSummary("Change the current user's password");

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
        RolePermissionResolver rolePermissionResolver,
        RoleManager<IdentityRole> roleManager)
    {
        var planRole = PortalRoles.NormalizePlanInput(request.PlanId);
        if (!PortalRoles.IsPlanRole(planRole) || !await roleManager.RoleExistsAsync(planRole))
        {
            return Results.BadRequest(new { errors = new[] { $"Plan '{request.PlanId}' does not exist." } });
        }

        var user = new ApplicationUser
        {
            Name = request.Name,
            Email = request.Email,
            UserName = request.Email
        };

        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        var roleResult = await userManager.AddToRoleAsync(user, planRole);
        if (!roleResult.Succeeded)
        {
            return Results.BadRequest(new { errors = roleResult.Errors.Select(e => e.Description) });
        }

        await signInManager.SignInAsync(user, isPersistent: true);

        SetXsrfTokenCookie(antiforgery, context);

        var permissions = await rolePermissionResolver.GetPermissionsAsync(user);
        var response = new AuthResponse(new UserResponse(user.Id, user.Name, user.Email!, [.. permissions]));
        return Results.Ok(response);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAntiforgery antiforgery,
        HttpContext context,
        RolePermissionResolver rolePermissionResolver)
    {
        var normalized = userManager.NormalizeEmail(request.Email);
        var user = await userManager.FindByEmailAsync(normalized);

        if (user is null)
        {
            return UnauthorizedError(
                "USER_NOT_FOUND",
                "No account exists for that email address.");
        }

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return UnauthorizedError("LOCKED_OUT", "This account is locked out.");
        }

        if (result.IsNotAllowed)
        {
            return UnauthorizedError(
                "NOT_ALLOWED",
                "Sign-in is not allowed for this account.");
        }

        if (!result.Succeeded)
        {
            return UnauthorizedError(
                "INVALID_PASSWORD",
                "The password is incorrect.");
        }

        SetXsrfTokenCookie(antiforgery, context);

        var permissions = await rolePermissionResolver.GetPermissionsAsync(user);
        var response = new AuthResponse(new UserResponse(user.Id, user.Name, user.Email!, [.. permissions]));
        return Results.Ok(response);
    }

    private static async Task<IResult> Logout(
        SignInManager<ApplicationUser> signInManager,
        IAntiforgery antiforgery,
        HttpContext context)
    {
        await signInManager.SignOutAsync();
        SetXsrfTokenCookie(antiforgery, context);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Results.Unauthorized();

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return Results.BadRequest(new { errors = result.Errors.Select(error => error.Description) });

        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RolePermissionResolver rolePermissionResolver)
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

        var permissions = await rolePermissionResolver.GetPermissionsAsync(user);
        var response = new UserResponse(user.Id, user.Name, user.Email!, [.. permissions]);
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

    private static IResult UnauthorizedError(string code, string message) =>
        Results.Json(
            new AuthErrorResponse(code, message),
            statusCode: StatusCodes.Status401Unauthorized);
}
