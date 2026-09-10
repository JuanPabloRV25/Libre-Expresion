namespace Portal.Application.Users;

public sealed record UserAreaDto(
    Guid Id,
    string Name,
    bool IsActive);

public sealed record UserRoleDto(
    Guid Id,
    string Name,
    bool IsActive,
    bool IsSystem);

public sealed record UserDto(
    Guid Id,
    string DocumentNumber,
    string FirstName,
    string LastName,
    string Email,
    UserAreaDto? Area,
    string? AdvisorCode,
    IReadOnlyList<UserRoleDto> Roles,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateUserCommand(
    string? DocumentNumber,
    string? FirstName,
    string? LastName,
    string? Email,
    Guid? AreaId,
    string? AdvisorCode,
    IReadOnlyList<Guid>? RoleIds,
    bool IsActive);

public sealed record UpdateUserCommand(
    string? FirstName,
    string? LastName,
    string? Email,
    Guid? AreaId,
    string? AdvisorCode);

public enum UserOperationStatus
{
    Success,
    NotFound,
    Invalid,
    DuplicateDocument,
    DuplicateEmail,
    LastSuperadmin,
    Conflict,
}

public sealed record UserOperationResult(
    UserOperationStatus Status,
    UserDto? User = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
