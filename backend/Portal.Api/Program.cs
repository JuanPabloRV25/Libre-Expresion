using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Portal.Api.Authentication;
using Portal.Api.Areas;
using Portal.Api.Audit;
using Portal.Api.Authorization;
using Portal.Api.Commercial.ProductionOrders;
using Portal.Api.Permissions;
using Portal.Api.Roles;
using Portal.Api.Users;
using Portal.Domain.Permissions;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var response = await client.GetAsync("http://127.0.0.1:8080/health");
        Environment.Exit(response.IsSuccessStatusCode ? 0 : 1);
    }
    catch
    {
        Environment.Exit(1);
    }

    return;
}

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    ConfigureProduction(builder.Configuration);
}

builder.Services.AddInfrastructure(
    builder.Configuration,
    builder.Environment.IsProduction());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    var configuredNetwork = builder.Configuration["PORTAL_TRUSTED_PROXY_CIDR"];
    if (!System.Net.IPNetwork.TryParse(configuredNetwork, out var trustedNetwork))
    {
        if (builder.Environment.IsProduction())
        {
            throw new InvalidOperationException(
                "PORTAL_TRUSTED_PROXY_CIDR must be a valid CIDR network.");
        }

        trustedNetwork = new System.Net.IPNetwork(
            IPAddress.Parse("172.29.10.0"), 24);
    }

    options.KnownIPNetworks.Add(trustedNetwork);
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = builder.Environment.IsProduction()
        ? "PortalLibreExpresion.Prod.Antiforgery"
        : "PortalLibreExpresion.Dev.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = 26L * 1024 * 1024);
builder.Services.AddAuthorization(options =>
{
    foreach (var permissionCode in PermissionCodes.All.Concat(CommercialPermissionCodes.All))
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

    if (app.Environment.IsProduction()
        && string.IsNullOrWhiteSpace(builder.Configuration["PORTAL_SUPERADMIN_PASSWORD_FILE"]))
    {
        throw new InvalidOperationException(
            "Production provisioning requires PORTAL_SUPERADMIN_PASSWORD_FILE.");
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
if (app.Environment.IsProduction())
{
    app.UseHsts();
}
app.UseAuthentication();
app.UseMiddleware<RequiredPasswordChangeMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapAreaEndpoints();
app.MapCommercialProductionOrderEndpoints();
Portal.Api.Commercial.Reports.ReportEndpoints.MapCommercialReportEndpoints(app);
app.MapAuditEndpoints();
app.MapPermissionEndpoints();
app.MapRoleEndpoints();
app.MapUserEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");
app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("ApiHealth");
app.MapGet("/api/ready", async (
    ApplicationDbContext db,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (await db.Database.CanConnectAsync(cancellationToken))
        {
            return Results.Ok(new { status = "Ready" });
        }
    }
    catch
    {
        // Readiness only exposes a status code, never connection details.
    }

    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).WithName("ApiReady");

app.Run();

static void ConfigureProduction(ConfigurationManager configuration)
{
    var allowedHosts = configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Contains('*'))
    {
        throw new InvalidOperationException(
            "Production requires explicit AllowedHosts.");
    }

    if (string.Equals(
        configuration["Authentication:UseDocumentAsTemporaryPassword"],
        "true",
        StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Document-based temporary passwords are disabled in production.");
    }

    if (!string.Equals(configuration["Email:Enabled"], "true", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(configuration["Email:Host"], "smtp.gmail.com", StringComparison.OrdinalIgnoreCase)
        || !string.Equals(configuration["Email:UseTls"], "true", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(configuration["Email:Username"])
        || !string.Equals(
            configuration["Email:Username"],
            configuration["Email:FromAddress"],
            StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Production requires the configured Gmail sender with STARTTLS.");
    }

    var portalBaseUrl = configuration["Email:PortalBaseUrl"];
    if (!Uri.TryCreate(portalBaseUrl, UriKind.Absolute, out var portalUri)
        || portalUri.Scheme != Uri.UriSchemeHttps)
    {
        throw new InvalidOperationException(
            "Production requires an HTTPS Email:PortalBaseUrl.");
    }

    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = Required(configuration, "PORTAL_DB_HOST"),
        Database = Required(configuration, "PORTAL_DB_NAME"),
        Username = Required(configuration, "PORTAL_DB_USER"),
        Password = ReadRequiredSecret(configuration, "PORTAL_DB_PASSWORD_FILE"),
    };
    configuration["ConnectionStrings:PortalDb"] = connectionString.ConnectionString;
    configuration["Email:Password"] = ReadRequiredSecret(
        configuration, "PORTAL_SMTP_PASSWORD_FILE");
}

static string Required(IConfiguration configuration, string key)
{
    var value = configuration[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"{key} is required.");
}

static string ReadRequiredSecret(IConfiguration configuration, string key)
{
    var path = Required(configuration, key);
    if (!Path.IsPathFullyQualified(path))
    {
        throw new InvalidOperationException($"{key} must be an absolute path.");
    }

    var value = File.ReadAllText(path).TrimEnd('\r', '\n');
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"{key} must not be empty.");
}

public partial class Program
{
}
