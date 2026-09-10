namespace Portal.Application.Permissions;

public sealed record PermissionDto(
    Guid Id,
    string Code,
    string Module,
    string Action,
    string DisplayName,
    string? Description,
    bool IsActive);
