namespace Portal.Application.Roles;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task<RoleDto?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<RoleOperationResult> CreateAsync(
        CreateRoleCommand command,
        CancellationToken cancellationToken = default);

    Task<RoleOperationResult> UpdateAsync(
        Guid id,
        UpdateRoleCommand command,
        CancellationToken cancellationToken = default);

    Task<RoleOperationResult> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<RoleOperationResult> UpdatePermissionsAsync(
        Guid id,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken = default);
}
