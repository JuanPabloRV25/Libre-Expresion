namespace Portal.Application.Notifications;

public interface INotificationService
{
    Task<NotificationResult> NotifyUserCreatedAsync(
        NotificationRecipient recipient,
        string encodedPasswordToken,
        CancellationToken cancellationToken = default);

    Task<NotificationResult> NotifyPasswordResetAsync(
        NotificationRecipient recipient,
        string encodedPasswordToken,
        CancellationToken cancellationToken = default);

    Task<NotificationResult> NotifyPasswordChangedAsync(
        NotificationRecipient recipient,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
