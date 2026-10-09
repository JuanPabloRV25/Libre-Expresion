using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Domain.Permissions;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed class DatabaseSeederTests
{
    private static readonly string[] ExpectedPermissionCodes =
    [
        "areas.view",
        "areas.create",
        "areas.edit",
        "areas.activate",
        "users.view",
        "users.create",
        "users.edit",
        "users.activate",
        "users.assign_roles",
        "users.reset_password",
        "roles.view",
        "roles.create",
        "roles.edit",
        "roles.activate",
        "roles.assign_permissions",
        "permissions.view",
        "audit.view",
    ];

    [Fact]
    public void Permission_catalog_contains_exactly_the_17_official_unique_codes()
    {
        Assert.Equal(17, PermissionCodes.All.Count);
        Assert.Equal(17, PermissionCodes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ExpectedPermissionCodes.Order(StringComparer.Ordinal),
            PermissionCodes.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Seeder_is_idempotent_and_does_not_reset_an_existing_superadmin()
    {
        await using var serviceProvider = CreateServiceProvider();
        var uniqueValue = Guid.NewGuid().ToString("N");
        var document = uniqueValue;
        var settings = new DatabaseSeedSettings(
            document,
            $"First{uniqueValue}",
            $"Last{uniqueValue}",
            $"{uniqueValue}@example.invalid");

        await SeedAsync(serviceProvider, settings);

        await using (var verificationScope = serviceProvider.CreateAsyncScope())
        {
            var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var passwordHasher = verificationScope.ServiceProvider
                .GetRequiredService<IPasswordHasher<ApplicationUser>>();

            var user = await context.Users.SingleAsync();
            var role = await context.Roles.SingleAsync(candidate => candidate.Name == DatabaseSeeder.SuperadminRoleName);

            Assert.Equal(DatabaseSeeder.SuperadminRoleName, role.Name);
            Assert.True(role.IsSystem);
            Assert.True(role.IsActive);
            Assert.True(user.IsActive);
            Assert.True(user.MustChangePassword);
            Assert.Null(user.AreaId);
            Assert.Null(user.AdvisorCode);
            Assert.NotNull(user.PasswordHash);
            Assert.NotEqual(
                PasswordVerificationResult.Failed,
                passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, document));

            Assert.Equal(PermissionCodes.All.Count + CommercialPermissionCodes.All.Count, await context.Permissions.CountAsync());
            var rolePermissions = await context.RolePermissions.ToListAsync();
            var userRoles = await context.Set<ApplicationUserRole>().ToListAsync();

            Assert.Equal(44, rolePermissions.Count); // Reports adds three permissions to Superadmin and Auxiliar Comercial.
            Assert.Single(userRoles);
            Assert.All(
                rolePermissions,
                rolePermission =>
                {
                    Assert.Null(rolePermission.AssignedByUserId);
                    Assert.NotEqual(default, rolePermission.AssignedAt);
                    Assert.Equal(TimeSpan.Zero, rolePermission.AssignedAt.Offset);
                });
            Assert.All(
                userRoles,
                userRole =>
                {
                    Assert.Null(userRole.AssignedByUserId);
                    Assert.NotEqual(default, userRole.AssignedAt);
                    Assert.Equal(TimeSpan.Zero, userRole.AssignedAt.Offset);
                });
            Assert.Empty(await context.Areas.ToListAsync());
            Assert.Empty(await context.AuditEvents.ToListAsync());

            var replacementCredential = Guid.NewGuid().ToString("N");
            user.PasswordHash = passwordHasher.HashPassword(user, replacementCredential);
            user.MustChangePassword = false;
            await context.SaveChangesAsync();
        }

        string passwordHashAfterUserUpdate;

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            passwordHashAfterUserUpdate = (await context.Users.SingleAsync()).PasswordHash!;
        }

        await SeedAsync(
            serviceProvider,
            new DatabaseSeedSettings(document, null, null, null));

        await using (var verificationScope = serviceProvider.CreateAsyncScope())
        {
            var context = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.SingleAsync();

            Assert.False(user.MustChangePassword);
            Assert.Equal(passwordHashAfterUserUpdate, user.PasswordHash);
            Assert.Equal(PermissionCodes.All.Count + CommercialPermissionCodes.All.Count, await context.Permissions.CountAsync());
            Assert.Equal(3, await context.Roles.CountAsync());
            Assert.Single(await context.Users.ToListAsync());
            Assert.Single(await context.Set<ApplicationUserRole>().ToListAsync());
            Assert.Equal(44, await context.RolePermissions.CountAsync());
            Assert.Empty(await context.Areas.ToListAsync());
            Assert.Empty(await context.AuditEvents.ToListAsync());
        }
    }

    [Fact]
    public async Task Seeder_uses_a_supplied_initial_password_instead_of_the_document()
    {
        await using var serviceProvider = CreateServiceProvider();
        var document = $"admin-{Guid.NewGuid():N}";
        const string initialPassword = "Seguro-para-inicio-2026!";
        var settings = new DatabaseSeedSettings(
            document,
            "Inicial",
            "Administrador",
            $"{Guid.NewGuid():N}@example.invalid",
            initialPassword);

        await SeedAsync(serviceProvider, settings);

        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordHasher = scope.ServiceProvider
            .GetRequiredService<IPasswordHasher<ApplicationUser>>();
        var user = await context.Users.SingleAsync();

        Assert.True(user.MustChangePassword);
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, initialPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, document));
    }

    private static async Task SeedAsync(
        ServiceProvider serviceProvider,
        DatabaseSeedSettings settings)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        await seeder.SeedAsync(settings);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        var databaseName = $"portal-seeder-{Guid.NewGuid():N}";

        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
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

        return services.BuildServiceProvider();
    }
}
