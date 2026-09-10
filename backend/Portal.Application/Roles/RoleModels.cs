namespace Portal.Application.Roles;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsSystem,
    int UserCount,
    IReadOnlyList<string> PermissionCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateRoleCommand(string? Name, string? Description);

public sealed record UpdateRoleCommand(string? Name, string? Description);

public enum RoleOperationStatus
{
    Success,
    NotFound,
    Invalid,
    Duplicate,
    Protected,
    Conflict,
}

public sealed record RoleOperationResult(
    RoleOperationStatus Status,
    RoleDto? Role = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
