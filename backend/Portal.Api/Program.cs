using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Portal.Api.Authentication;
using Portal.Api.Areas;
using Portal.Api.Audit;
using Portal.Api.Authorization;
using Portal.Api.Permissions;
using Portal.Api.Roles;
using Portal.Api.Users;
using Portal.Domain.Permissions;
using Portal.Infrastructure;
using Portal.Infrastructure.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(
    builder.Configuration,
    builder.Environment.IsProduction());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Add(
        new System.Net.IPNetwork(IPAddress.Parse("172.29.10.0"), 24));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "PortalLibreExpresion.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddAuthorization(options =>
{
    foreach (var permissionCode in PermissionCodes.All)
    {
        options.AddPolicy(
            permissionCode,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permissionCode)));
    }
});
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

var app = builder.Build();

var portalOperation = builder.Configuration["PORTAL_OPERATION"];
var seedingEnabled = DatabaseSeeding.IsEnabled(builder.Configuration);

if (string.Equals(portalOperation, "provision", StringComparison.OrdinalIgnoreCase))
{
    if (!seedingEnabled)
    {
        throw new InvalidOperationException(
            "Explicit provisioning requires DATABASE_SEEDING_ENABLED=true.");
    }

    await app.Services.SeedDatabaseAsync(builder.Configuration);
    return;
}

if (seedingEnabled)
{
    throw new InvalidOperationException(
        "Database seeding is disabled during normal API startup. Use the explicit provisioning operation.");
}

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseMiddleware<RequiredPasswordChangeMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapAreaEndpoints();
app.MapAuditEndpoints();
app.MapPermissionEndpoints();
app.MapRoleEndpoints();
app.MapUserEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");
app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("ApiHealth");

app.Run();

public partial class Program
{
}
