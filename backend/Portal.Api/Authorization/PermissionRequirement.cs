using Microsoft.AspNetCore.Authorization;

namespace Portal.Api.Authorization;

public sealed record PermissionRequirement(string PermissionCode)
    : IAuthorizationRequirement;
