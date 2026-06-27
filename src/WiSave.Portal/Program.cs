using WiSave.Portal.Auth;
using WiSave.Portal.Authorization;
using WiSave.Portal.Endpoints;
using WiSave.Portal.Gateway;
using WiSave.Portal.Hubs;
using WiSave.Portal.Infrastructure;
using WiSave.Portal.Messaging;
using WiSave.Portal.Session;

var builder = WebApplication.CreateBuilder(args);

builder.AddPortalMessaging();

builder.Services.AddPortalIdentity(builder.Configuration, builder.Environment);
builder.Services.AddPortalAntiforgery(builder.Environment);
builder.Services.AddPortalAuthRateLimiting();
builder.Services.AddPortalAuthorization();
builder.Services.AddPortalSession(builder.Configuration);
builder.Services.AddPortalGateway(builder.Configuration);
builder.Services.AddPortalSignalR(builder.Configuration);
builder.Services.AddPortalOpenApi();

var corsOrigins = builder.Configuration.GetCorsOrigins();

builder.Services.AddPortalCors(corsOrigins);

var app = builder.Build();

app.MapPortalApiDocs();
app.UsePortalCors(corsOrigins);
app.UsePortalForwarding();
app.UseRateLimiter();
app.UsePortalAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapAdminAccessManagementEndpoints();
app.MapPortalHubs();
app.MapPortalReverseProxy();

app.Run();

public partial class Program { }
