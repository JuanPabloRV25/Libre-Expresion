namespace Portal.Application.Permissions;

public interface IPermissionService
{
    Task<IReadOnlyList<PermissionDto>> ListAsync(
        CancellationToken cancellationToken = default);
}
