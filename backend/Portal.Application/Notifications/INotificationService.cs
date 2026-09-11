namespace Portal.Application.Notifications;

public interface INotificationService
{
    Task<NotificationResult> NotifyUserCreatedAsync(
        NotificationRecipient recipient,
        CancellationToken cancellationToken = default);

    Task<NotificationResult> NotifyPasswordResetAsync(
        NotificationRecipient recipient,
        CancellationToken cancellationToken = default);

    Task<NotificationResult> NotifyPasswordChangedAsync(
        NotificationRecipient recipient,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
