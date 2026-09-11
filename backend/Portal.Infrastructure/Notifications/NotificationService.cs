using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Application.Notifications;

namespace Portal.Infrastructure.Notifications;

public sealed class NotificationService(
    IEmailSender emailSender,
    IOptions<EmailOptions> options,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly EmailOptions settings = options.Value;

    public Task<NotificationResult> NotifyUserCreatedAsync(
        NotificationRecipient recipient,
        CancellationToken cancellationToken = default) => SendAsync(
            UserCreatedEmail.Create(recipient, CreateContext()),
            cancellationToken);

    public Task<NotificationResult> NotifyPasswordResetAsync(
        NotificationRecipient recipient,
        CancellationToken cancellationToken = default) => SendAsync(
            PasswordResetEmail.Create(recipient, CreateContext()),
            cancellationToken);

    public Task<NotificationResult> NotifyPasswordChangedAsync(
        NotificationRecipient recipient,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default) => SendAsync(
            PasswordChangedEmail.Create(recipient, CreateContext(), occurredAt),
            cancellationToken);

    private async Task<NotificationResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            logger.LogWarning("Email notification delivery is disabled by configuration.");
            return NotificationResult.Failed;
        }

        try
        {
            await emailSender.SendAsync(message, cancellationToken);
            return NotificationResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Email notification delivery failed after the business operation completed.");
            return NotificationResult.Failed;
        }
    }

    private EmailTemplateContext CreateContext() => new(
        settings.PortalBaseUrl,
        settings.EnvironmentLabel);
}
