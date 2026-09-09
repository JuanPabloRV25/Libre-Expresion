using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
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
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
