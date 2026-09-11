namespace Portal.Application.Identity;

public interface IPortalAuthenticationService
{
    Task<PortalLoginResult> LoginAsync(
        string documentNumber,
        string password);

    Task<PasswordChangeResult> ChangeRequiredPasswordAsync(
        Guid userId,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default);

    Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default);

    Task<PasswordResetCompletionResult> CompletePasswordResetAsync(
        Guid userId,
        string encodedToken,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default);

    Task SignOutAsync();
}
