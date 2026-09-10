namespace Portal.Application.Identity;

public interface ICurrentUserService
{
    Task<CurrentUserInfo?> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(
        string permissionCode,
        CancellationToken cancellationToken = default);
}
