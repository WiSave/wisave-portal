using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using WiSave.Portal.Auth;
using WiSave.Portal.Authorization;
using WiSave.Portal.Endpoints;
using WiSave.Portal.Gateway;
using WiSave.Portal.Hubs;
using WiSave.Portal.Messaging;
using WiSave.Portal.Session;
using WiSave.Portal.Migrations;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPortalIdentity(builder.Configuration, builder.Environment);
builder.Services.AddPortalSession(builder.Configuration);
builder.Services.AddPortalGateway(builder.Configuration);
builder.Services.AddPortalSignalR();
builder.Services.AddPortalMessaging(builder.Configuration);

builder.Services.AddSingleton<UserPlanCache>();
builder.Services.AddSingleton<PlanPermissionCache>();

builder.Services.AddOpenApi();
builder.Services.AddPermissionPolicies();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    // The system's own cookie stores the cookie token (HttpOnly).
    // A separate XSRF-TOKEN cookie with the request token is set manually
    // after GetAndStoreTokens() for Angular's XSRF interceptor to read.
    options.Cookie.Name = ".AspNetCore.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth-login", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("auth-register", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

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

if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    });
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<PermissionResolutionMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapPortalHubs();

app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use(async (context, next) =>
    {
        string[] unsafeMethods = ["POST", "PUT", "DELETE", "PATCH"];
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
