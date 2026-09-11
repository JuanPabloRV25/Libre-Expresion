namespace Portal.Application.Identity;

public enum PortalLoginStatus
{
    Succeeded,
    InvalidCredentials,
    Inactive,
    LockedOut,
}

public sealed record PortalLoginResult(
    PortalLoginStatus Status,
    bool MustChangePassword = false);

public enum PasswordChangeStatus
{
    Succeeded,
    UserNotFound,
    Inactive,
    ChangeNotRequired,
    ChangeRequired,
    ConfirmationMismatch,
    InvalidCurrentPassword,
    InvalidPassword,
}

public sealed record PasswordChangeResult(
    PasswordChangeStatus Status,
    string? NotificationStatus = null);
