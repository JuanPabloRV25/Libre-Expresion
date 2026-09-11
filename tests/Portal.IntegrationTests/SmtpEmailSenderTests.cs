using Microsoft.Extensions.Options;
using Portal.Application.Notifications;
using Portal.Infrastructure.Notifications;

namespace Portal.IntegrationTests;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public void Mime_message_uses_configured_sender_and_requested_recipient()
    {
        var settings = new EmailOptions
        {
            Enabled = true,
            Host = "mailpit",
            Port = 1025,
            FromName = "Portal Libre Expresión",
            FromAddress = "notificaciones@libreexpresion.test",
            PortalBaseUrl = "http://127.0.0.1:5173",
            EnvironmentLabel = "DESARROLLO",
        };
        var message = new EmailMessage(
            "usuario.prueba@libreexpresion.test",
            "Asunto",
            "<p>HTML</p>",
            "Texto");

        var mimeMessage = SmtpEmailSender.BuildMimeMessage(message, settings);

        Assert.Equal("Portal Libre Expresión", mimeMessage.From.Mailboxes.Single().Name);
        Assert.Equal("notificaciones@libreexpresion.test", mimeMessage.From.Mailboxes.Single().Address);
        Assert.Equal(message.To, mimeMessage.To.Mailboxes.Single().Address);
        Assert.Contains("HTML", mimeMessage.HtmlBody);
        Assert.Equal("Texto", mimeMessage.TextBody);
    }
}
