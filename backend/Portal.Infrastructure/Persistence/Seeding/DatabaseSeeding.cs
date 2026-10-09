using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Portal.Infrastructure.Persistence.Seeding;

public static class DatabaseSeeding
{
    public const string EnabledConfigurationKey = "DATABASE_SEEDING_ENABLED";

    public static bool IsEnabled(IConfiguration configuration)
    {
        var configuredValue = configuration[EnabledConfigurationKey];

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return false;
        }

        return bool.TryParse(configuredValue, out var enabled)
            ? enabled
            : throw new InvalidOperationException(
                $"{EnabledConfigurationKey} must be configured as true or false.");
    }

    public static async Task SeedDatabaseAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var scope = services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        string? initialPassword = null;
        var passwordFile = configuration["PORTAL_SUPERADMIN_PASSWORD_FILE"];
        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            if (!Path.IsPathFullyQualified(passwordFile))
            {
                throw new InvalidOperationException(
                    "PORTAL_SUPERADMIN_PASSWORD_FILE must be an absolute path.");
            }

            initialPassword = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
        }

        var settings = new DatabaseSeedSettings(
            configuration["SUPERADMIN_DOCUMENT"],
            configuration["SUPERADMIN_FIRST_NAME"],
            configuration["SUPERADMIN_LAST_NAME"],
            configuration["SUPERADMIN_EMAIL"],
            initialPassword);

        await seeder.SeedAsync(settings, cancellationToken);
    }
}
