using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Identity;

public sealed class CurrentUserService(
    IHttpContextAccessor httpContextAccessor,
    ApplicationDbContext dbContext) : ICurrentUserService
{
    private Task<CurrentUserInfo?>? currentUserTask;

    public Task<CurrentUserInfo?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        currentUserTask ??= LoadCurrentAsync(cancellationToken);
        return currentUserTask;
    }

    public async Task<bool> HasPermissionAsync(
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentAsync(cancellationToken);

        return currentUser is { IsActive: true, MustChangePassword: false }
            && currentUser.Permissions.Contains(permissionCode, StringComparer.Ordinal);
    }

    private async Task<CurrentUserInfo?> LoadCurrentAsync(
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var userIdValue = principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(userIdValue, out var userId))
        {
            return null;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        CurrentUserArea? area = null;

        if (user.AreaId is { } areaId)
        {
            area = await dbContext.Areas
                .AsNoTracking()
                .Where(candidate => candidate.Id == areaId)
                .Select(candidate => new CurrentUserArea(candidate.Id, candidate.Name))
                .SingleOrDefaultAsync(cancellationToken);
        }

        var activeRoles = await (
                from userRole in dbContext.Set<ApplicationUserRole>().AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where userRole.UserId == user.Id && role.IsActive
                orderby role.Name
                select new { role.Id, role.Name })
            .ToListAsync(cancellationToken);

        var availableRoleIds = activeRoles.Select(role => role.Id).ToArray();
        var claimedRoleIds = principal.FindAll("portal:active_role")
            .Select(claim => Guid.TryParse(claim.Value, out var roleId) ? roleId : Guid.Empty)
            .Where(roleId => roleId != Guid.Empty)
            .Distinct()
            .ToArray();
        var roleIds = (claimedRoleIds.Length == 0 ? availableRoleIds : claimedRoleIds)
            .Where(availableRoleIds.Contains)
            .ToArray();
        IReadOnlyList<string> permissions = [];

        if (!user.MustChangePassword && roleIds.Length > 0)
        {
            permissions = await (
                    from rolePermission in dbContext.RolePermissions.AsNoTracking()
                    join permission in dbContext.Permissions.AsNoTracking()
                        on rolePermission.PermissionId equals permission.Id
                    where roleIds.Contains(rolePermission.RoleId) && permission.IsActive
                    orderby permission.Code
                    select permission.Code)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        }

        return new CurrentUserInfo(
            user.Id,
            user.UserName ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.Email ?? string.Empty,
            area,
            activeRoles
                .Select(role => role.Name ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToArray(),
            activeRoles.Select(role => new CurrentUserRole(role.Id, role.Name ?? string.Empty)).ToArray(),
            roleIds,
            permissions,
            user.IsActive,
            user.MustChangePassword);
    }
}
