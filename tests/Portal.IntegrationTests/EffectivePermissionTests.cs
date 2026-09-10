using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Api.Authorization;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.IntegrationTests;

public sealed class EffectivePermissionTests
{
    [Fact]
    public async Task CurrentUser_UnionsActiveRolesAndPermissions_AndPolicyUsesThatResult()
    {
        await using var factory = new PortalApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
        await services.GetRequiredService<DatabaseSeeder>().SeedAsync(
            new DatabaseSeedSettings(
                $"8{Random.Shared.NextInt64(100000000, 999999999)}",
                "Permission",
                "Bootstrap",
                $"bootstrap-{Guid.NewGuid():N}@example.test"));

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "permission-test-user",
            NormalizedUserName = "PERMISSION-TEST-USER",
            FirstName = "Permission",
            LastName = "Test",
            Email = "permission-user@example.test",
            NormalizedEmail = "PERMISSION-USER@EXAMPLE.TEST",
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var firstActiveRole = Role("First active", isActive: true, now);
        var secondActiveRole = Role("Second active", isActive: true, now);
        var inactiveRole = Role("Inactive role", isActive: false, now);
        var areaView = await dbContext.Permissions.SingleAsync(permission => permission.Code == PermissionCodes.AreasView);
        var usersView = await dbContext.Permissions.SingleAsync(permission => permission.Code == PermissionCodes.UsersView);
        var rolesView = await dbContext.Permissions.SingleAsync(permission => permission.Code == PermissionCodes.RolesView);
        var auditView = await dbContext.Permissions.SingleAsync(permission => permission.Code == PermissionCodes.AuditView);
        rolesView.IsActive = false;

        dbContext.Users.Add(user);
        dbContext.Roles.AddRange(firstActiveRole, secondActiveRole, inactiveRole);
        dbContext.Set<ApplicationUserRole>().AddRange(
            UserRole(user.Id, firstActiveRole.Id, now),
            UserRole(user.Id, secondActiveRole.Id, now),
            UserRole(user.Id, inactiveRole.Id, now));
        dbContext.RolePermissions.AddRange(
            RolePermission(firstActiveRole.Id, areaView.Id, now),
            RolePermission(secondActiveRole.Id, areaView.Id, now),
            RolePermission(secondActiveRole.Id, usersView.Id, now),
            RolePermission(secondActiveRole.Id, rolesView.Id, now),
            RolePermission(inactiveRole.Id, auditView.Id, now));
        await dbContext.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        ], "test-cookie"));
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };
        var currentUserService = new CurrentUserService(accessor, dbContext);

        var currentUser = await currentUserService.GetCurrentAsync();
        Assert.NotNull(currentUser);
        Assert.Equal(["First active", "Second active"], currentUser.Roles);
        Assert.Equal(
            [PermissionCodes.AreasView, PermissionCodes.UsersView],
            currentUser.Permissions);

        var allowedRequirement = new PermissionRequirement(PermissionCodes.AreasView);
        var allowedContext = new AuthorizationHandlerContext([allowedRequirement], principal, null);
        await new PermissionAuthorizationHandler(currentUserService).HandleAsync(allowedContext);
        Assert.True(allowedContext.HasSucceeded);

        var deniedRequirement = new PermissionRequirement(PermissionCodes.AuditView);
        var deniedContext = new AuthorizationHandlerContext([deniedRequirement], principal, null);
        await new PermissionAuthorizationHandler(currentUserService).HandleAsync(deniedContext);
        Assert.False(deniedContext.HasSucceeded);
    }

    private static ApplicationRole Role(string name, bool isActive, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Description = name,
        IsActive = isActive,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static ApplicationUserRole UserRole(Guid userId, Guid roleId, DateTimeOffset now) => new()
    {
        UserId = userId,
        RoleId = roleId,
        AssignedAt = now,
    };

    private static RolePermission RolePermission(Guid roleId, Guid permissionId, DateTimeOffset now) => new()
    {
        RoleId = roleId,
        PermissionId = permissionId,
        AssignedAt = now,
    };
}
