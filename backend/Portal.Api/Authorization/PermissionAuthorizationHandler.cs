using Microsoft.AspNetCore.Authorization;
using Portal.Application.Identity;

namespace Portal.Api.Authorization;

public sealed class PermissionAuthorizationHandler(
    ICurrentUserService currentUserService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (await currentUserService.HasPermissionAsync(requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }
    }
}
