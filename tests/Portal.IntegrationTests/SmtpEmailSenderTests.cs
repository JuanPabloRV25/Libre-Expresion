using Microsoft.Extensions.Options;
using MailKit.Security;
using MimeKit;
using Portal.Infrastructure;
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

    [Fact]
    public void Mime_message_embeds_the_portal_logo_when_template_uses_its_content_id()
    {
        var message = new EmailMessage(
            "usuario.prueba@libreexpresion.test",
            "Asunto",
            "<html><body><img src=\"cid:portal-logo\"></body></html>",
            "Texto");

        var mimeMessage = SmtpEmailSender.BuildMimeMessage(message, ValidEmailOptions());
        var logo = mimeMessage.BodyParts
            .OfType<MimePart>()
            .Single(part => part.ContentId == "portal-logo");

        Assert.Equal("image/png", logo.ContentType.MimeType);
        Assert.Equal(ContentDisposition.Inline, logo.ContentDisposition?.Disposition);
    }

    [Fact]
    public void Socket_security_mode_uses_configuration_only()
    {
        Assert.Equal(
            SecureSocketOptions.None,
            SmtpEmailSender.ResolveSocketOptions(new EmailOptions()));
        Assert.Equal(
            SecureSocketOptions.StartTls,
            SmtpEmailSender.ResolveSocketOptions(new EmailOptions
            {
                UseTls = true,
            }));
        Assert.Equal(
            SecureSocketOptions.SslOnConnect,
            SmtpEmailSender.ResolveSocketOptions(new EmailOptions
            {
                UseSsl = true,
            }));
    }

    [Fact]
    public void Email_configuration_requires_encryption_when_credentials_are_present()
    {
        Assert.True(DependencyInjection.IsValidEmailConfiguration(
            ValidEmailOptions()));
        Assert.False(DependencyInjection.IsValidEmailConfiguration(
            ValidEmailOptions(
                username: "smtp-user",
                password: "smtp-password")));
        Assert.True(DependencyInjection.IsValidEmailConfiguration(
            ValidEmailOptions(
                useTls: true,
                username: "smtp-user",
                password: "smtp-password")));
        Assert.True(DependencyInjection.IsValidEmailConfiguration(
            ValidEmailOptions(
                useSsl: true,
                username: "smtp-user",
                password: "smtp-password")));
    }

    private static EmailOptions ValidEmailOptions(
        bool useTls = false,
        bool useSsl = false,
        string username = "",
        string password = "") => new()
        {
            Enabled = true,
            Host = "mailpit",
            Port = 1025,
            UseTls = useTls,
            UseSsl = useSsl,
            Username = username,
            Password = password,
            FromName = "Portal Libre Expresión",
            FromAddress = "notificaciones@libreexpresion.test",
            PortalBaseUrl = "http://127.0.0.1:5173",
            EnvironmentLabel = "DESARROLLO",
        };
}
