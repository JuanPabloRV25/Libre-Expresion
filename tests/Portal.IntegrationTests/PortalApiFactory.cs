using Microsoft.AspNetCore.Hosting;
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
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }
}
