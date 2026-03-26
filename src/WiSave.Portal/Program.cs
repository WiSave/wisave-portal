using WiSave.Portal.Auth;
using WiSave.Portal.Authorization;
using WiSave.Portal.Endpoints;
using WiSave.Portal.Gateway;
using WiSave.Portal.Session;
using WiSave.Portal.Migrations;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPortalIdentity(builder.Configuration);
builder.Services.AddPortalSession(builder.Configuration);
builder.Services.AddPortalGateway(builder.Configuration);

builder.Services.AddSingleton<UserPlanCache>();
builder.Services.AddSingleton<PlanPermissionCache>();

builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });
}

var app = builder.Build();

var autoApplyMigrations = builder.Configuration.GetValue<bool>("Migrations:AutoApplyOnStartup");
var useInMemoryDatabase = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");
var connectionString = builder.Configuration.GetConnectionString("Portal");

if (autoApplyMigrations && !useInMemoryDatabase && !string.IsNullOrWhiteSpace(connectionString))
{
    DbMigrator.Run(connectionString);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (corsOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<PermissionResolutionMiddleware>();
app.UseAntiforgery();

app.MapAuthEndpoints();

app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use(async (context, next) =>
    {
        var unsafeMethods = new[] { "POST", "PUT", "DELETE", "PATCH" };
        if (unsafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
        {
            var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Antiforgery token validation failed");
                return;
            }
        }
        await next();
    });
    proxyPipeline.UseSessionAffinity();
    proxyPipeline.UseLoadBalancing();
    proxyPipeline.UsePassiveHealthChecks();
});

app.Run();

public partial class Program { }
