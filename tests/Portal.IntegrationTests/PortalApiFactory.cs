using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portal.Application.Notifications;
using Portal.Infrastructure.Persistence;

namespace Portal.IntegrationTests;

public sealed class PortalApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"portal-auth-{Guid.NewGuid():N}";
    private readonly string documentRoot = Path.Combine(Path.GetTempPath(), $"portal-documents-{Guid.NewGuid():N}");
    private readonly TimeSpan? passwordResetTokenLifespan;

    private readonly string? postgresConnectionString;
    private readonly Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor? saveInterceptor;
    private readonly bool disableCommercialOutboxWorker;

    public PortalApiFactory(TimeSpan? passwordResetTokenLifespan = null, string? postgresConnectionString = null,
        Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor? saveInterceptor = null, bool disableCommercialOutboxWorker = false)
    {
        this.passwordResetTokenLifespan = passwordResetTokenLifespan;
        this.postgresConnectionString = postgresConnectionString;
        this.saveInterceptor = saveInterceptor;
        this.disableCommercialOutboxWorker = disableCommercialOutboxWorker;
    }

    public TestEmailSender EmailSender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_SEEDING_ENABLED"] = "false",
                ["ConnectionStrings:PortalDb"] = string.Empty,
                ["Email:Enabled"] = "true",
                ["Email:Host"] = "smtp.example.test",
                ["Email:Port"] = "1025",
                ["Email:UseTls"] = "false",
                ["Email:FromName"] = "Portal Libre Expresión",
                ["Email:FromAddress"] = "notificaciones@libreexpresion.test",
                ["Email:PortalBaseUrl"] = "http://127.0.0.1:5173",
                ["Email:EnvironmentLabel"] = "DESARROLLO",
                ["CommercialDocuments:RootPath"] = documentRoot,
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (postgresConnectionString is null) options.UseInMemoryDatabase(databaseName);
                else options.UseNpgsql(postgresConnectionString);
                if (saveInterceptor is not null) options.AddInterceptors(saveInterceptor);
            });
            if (disableCommercialOutboxWorker)
            {
                var worker = services.FirstOrDefault(service => service.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                    && service.ImplementationType == typeof(Portal.Infrastructure.Commercial.ProductionOrders.CommercialNotificationOutboxWorker));
                if (worker is not null) services.Remove(worker);
            }

            if (passwordResetTokenLifespan.HasValue)
            {
                services.PostConfigure<DataProtectionTokenProviderOptions>(options =>
                    options.TokenLifespan = passwordResetTokenLifespan.Value);
            }

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }
}
