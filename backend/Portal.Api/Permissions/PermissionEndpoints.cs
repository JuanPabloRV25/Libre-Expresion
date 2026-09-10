using Portal.Application.Permissions;
using Portal.Domain.Permissions;

namespace Portal.Api.Permissions;

public static class PermissionEndpoints
{
    public static IEndpointRouteBuilder MapPermissionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/permissions",
                async (
                    IPermissionService permissionService,
                    CancellationToken cancellationToken) => Results.Ok(
                        await permissionService.ListAsync(cancellationToken)))
            .RequireAuthorization(PermissionCodes.PermissionsView);

        return endpoints;
    }
}
