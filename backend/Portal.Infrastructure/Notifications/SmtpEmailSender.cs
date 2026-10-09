using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Portal.Application.Notifications;

namespace Portal.Infrastructure.Notifications;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions settings = options.Value;

    public async Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Email delivery is disabled.");
        }

        var mimeMessage = BuildMimeMessage(message, settings);
        using var client = new SmtpClient();
        var socketOptions = ResolveSocketOptions(settings);

        await client.ConnectAsync(
            settings.Host,
            settings.Port,
            socketOptions,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            await client.AuthenticateAsync(
                settings.Username,
                settings.Password,
                cancellationToken);
        }

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    internal static MimeMessage BuildMimeMessage(
        EmailMessage message,
        EmailOptions settings)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(
            settings.FromName,
            settings.FromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.To));
        mimeMessage.Subject = message.Subject;
        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        };

        if (message.HtmlBody.Contains("cid:portal-logo", StringComparison.OrdinalIgnoreCase))
        {
            const string resourceName = "Portal.EmailLogo";
            using var logoStream = typeof(SmtpEmailSender).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded email asset '{resourceName}' was not found.");
            var logo = bodyBuilder.LinkedResources.Add("logo-horizontal.png", logoStream);
            logo.ContentId = "portal-logo";
            logo.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();
        return mimeMessage;
    }

    internal static SecureSocketOptions ResolveSocketOptions(EmailOptions settings) =>
        settings.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : settings.UseTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;
}
