using Microsoft.EntityFrameworkCore;
using Portal.Application.Permissions;
using Portal.Domain.Permissions;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Permissions;

public sealed class PermissionService(ApplicationDbContext dbContext)
    : IPermissionService
{
    public async Task<IReadOnlyList<PermissionDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var officialCodes = PermissionCodes.All.Concat(CommercialPermissionCodes.All).ToArray();
        return await dbContext.Permissions
            .AsNoTracking()
            .Where(permission => officialCodes.Contains(permission.Code))
            .OrderBy(permission => permission.Module)
            .ThenBy(permission => permission.Code)
            .Select(permission => new PermissionDto(
                permission.Id,
                permission.Code,
                permission.Module,
                permission.Action,
                permission.DisplayName,
                permission.Description,
                permission.IsActive))
            .ToArrayAsync(cancellationToken);
    }
}
