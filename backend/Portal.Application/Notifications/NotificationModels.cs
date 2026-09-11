namespace Portal.Application.Notifications;

public sealed record NotificationRecipient(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string DocumentNumber);

public sealed record EmailTemplateContext(
    string PortalBaseUrl,
    string EnvironmentLabel);

public sealed record NotificationResult(string Status)
{
    public static NotificationResult Sent { get; } = new(NotificationStatuses.Sent);

    public static NotificationResult Failed { get; } = new(NotificationStatuses.Failed);
}

public static class NotificationStatuses
{
    public const string Sent = "sent";
    public const string Failed = "failed";
}

public static class NotificationAuditActions
{
    public const string UserCreatedSent = "notification.user_created.sent";
    public const string UserCreatedFailed = "notification.user_created.failed";
    public const string PasswordResetSent = "notification.password_reset.sent";
    public const string PasswordResetFailed = "notification.password_reset.failed";
    public const string PasswordChangedSent = "notification.password_changed.sent";
    public const string PasswordChangedFailed = "notification.password_changed.failed";
}
