using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WiSave.Portal.Auth.Models;
using WiSave.Portal.Infrastructure.Database;

namespace WiSave.Portal.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth");

        group.MapPost("/register", Register)
            .DisableAntiforgery()
            .Produces<AuthResponse>()
            .ProducesValidationProblem()
            .WithSummary("Register a new user account");

        group.MapPost("/login", Login)
            .DisableAntiforgery()
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
            antiforgery.GetAndStoreTokens(context);
            return Results.Ok();
        })
            .RequireAuthorization()
            .Produces(200)
            .WithSummary("Get XSRF token cookie");
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAntiforgery antiforgery,
        HttpContext context,
        PortalDbContext db)
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
        
        antiforgery.GetAndStoreTokens(context);

        var response = new AuthResponse(new UserResponse(user.Id, user.Name, user.Email));
        return Results.Ok(response);
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAntiforgery antiforgery,
        HttpContext context)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            return Results.Unauthorized();
        }
        
        antiforgery.GetAndStoreTokens(context);

        var response = new AuthResponse(new UserResponse(user.Id, user.Name, user.Email));
        return Results.Ok(response);
    }

    private static async Task<IResult> Logout(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        HttpContext context,
        UserManager<ApplicationUser> userManager)
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

        var response = new UserResponse(user.Id, user.Name, user.Email!);
        return Results.Ok(response);
    }
}
