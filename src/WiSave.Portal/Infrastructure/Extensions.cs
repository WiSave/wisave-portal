using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using WiSave.Portal.Migrations;

namespace WiSave.Portal.Infrastructure;

public static class Extensions
{
    public static IServiceCollection AddPortalOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi();
        return services;
    }

    public static string[] GetCorsOrigins(this IConfiguration configuration)
    {
        return configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    }

    public static IServiceCollection AddPortalCors(this IServiceCollection services, string[] corsOrigins)
    {
        if (corsOrigins.Length == 0)
        {
            return services;
        }

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(corsOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }

    public static WebApplication ApplyPortalMigrations(this WebApplication app)
    {
        var autoApplyMigrations = app.Configuration.GetValue<bool>("Migrations:AutoApplyOnStartup");
        var useInMemoryDatabase = app.Configuration.GetValue<bool>("UseInMemoryDatabase");
        var connectionString = app.Configuration.GetConnectionString("Portal");

        if (autoApplyMigrations && !useInMemoryDatabase && !string.IsNullOrWhiteSpace(connectionString))
        {
            DbMigrator.Run(connectionString);
        }

        return app;
    }

    public static WebApplication MapPortalApiDocs(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapOpenApi();
        app.MapScalarApiReference();

        return app;
    }

    public static WebApplication UsePortalCors(this WebApplication app, string[] corsOrigins)
    {
        if (corsOrigins.Length > 0)
        {
            app.UseCors();
        }

        return app;
    }

    public static WebApplication UsePortalForwarding(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return app;
        }

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        });

        return app;
    }
}
