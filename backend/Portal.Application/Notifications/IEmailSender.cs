namespace Portal.Application.Notifications;

public interface IEmailSender
{
    Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default);
}
