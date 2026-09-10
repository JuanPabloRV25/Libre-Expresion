namespace Portal.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(
        string? search,
        bool? isActive,
        Guid? areaId,
        Guid? roleId,
        CancellationToken cancellationToken = default);

    Task<UserDto?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<UserOperationResult> CreateAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default);

    Task<UserOperationResult> UpdateAsync(
        Guid id,
        UpdateUserCommand command,
        CancellationToken cancellationToken = default);

    Task<UserOperationResult> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<UserOperationResult> UpdateRolesAsync(
        Guid id,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken = default);

    Task<UserOperationResult> ResetPasswordAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
