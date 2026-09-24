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

public enum ActiveRoleSelectionStatus
{
    Succeeded,
    InvalidSelection,
    UserNotFound,
}

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

public enum PasswordResetCompletionStatus
{
    Succeeded,
    InvalidToken,
    ConfirmationMismatch,
    InvalidPassword,
}

public sealed record PasswordResetCompletionResult(
    PasswordResetCompletionStatus Status,
    string? NotificationStatus = null);
