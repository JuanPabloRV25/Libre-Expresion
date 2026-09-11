using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Application.Identity;
using Portal.Application.Auditing;
using Portal.Application.Areas;
using Portal.Application.Permissions;
using Portal.Application.Notifications;
using Portal.Application.Roles;
using Portal.Application.Users;
using Portal.Infrastructure.Areas;
using Portal.Infrastructure.Auditing;
using Portal.Infrastructure.Permissions;
using Portal.Infrastructure.Roles;
using Portal.Infrastructure.Users;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;
using Portal.Infrastructure.Notifications;

namespace Portal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireSecureCookies = false)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("PortalDb");

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql(connectionString);
            }
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<ApplicationRole>()
            .AddSignInManager()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(2));

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = "PortalLibreExpresion.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = requireSecureCookies
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.EventsType = typeof(PortalCookieAuthenticationEvents);
            });

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                options => !options.Enabled || IsValidEmailConfiguration(options),
                "Email configuration is incomplete or invalid.")
            .ValidateOnStart();
        services.AddScoped<DatabaseSeeder>();
        services.AddHttpContextAccessor();
        services.AddScoped<PortalCookieAuthenticationEvents>();
        services.AddScoped<IPortalAuthenticationService, PortalAuthenticationService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAreaService, AreaService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }

    internal static bool IsValidEmailConfiguration(EmailOptions options) =>
        !string.IsNullOrWhiteSpace(options.Host)
        && options.Port is > 0 and <= 65_535
        && !(options.UseTls && options.UseSsl)
        && (string.IsNullOrWhiteSpace(options.Username)
            == string.IsNullOrWhiteSpace(options.Password))
        && (string.IsNullOrWhiteSpace(options.Username)
            || options.UseTls
            || options.UseSsl)
        && !string.IsNullOrWhiteSpace(options.FromName)
        && System.Net.Mail.MailAddress.TryCreate(options.FromAddress, out _)
        && Uri.TryCreate(options.PortalBaseUrl, UriKind.Absolute, out var portalUri)
        && (portalUri.Scheme == Uri.UriSchemeHttp
            || portalUri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(options.EnvironmentLabel);
}
