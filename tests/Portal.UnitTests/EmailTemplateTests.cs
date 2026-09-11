using Portal.Application.Notifications;

namespace Portal.UnitTests;

public sealed class EmailTemplateTests
{
    private static readonly NotificationRecipient Recipient = new(
        Guid.Parse("834a7b6f-ea1c-44bc-82f8-e153146ad286"),
        "usuario.prueba@libreexpresion.test",
        "María",
        "Pruebas",
        "8809100001");

    private static readonly EmailTemplateContext Context = new(
        "http://127.0.0.1:5173/",
        "DESARROLLO");

    [Fact]
    public void UserCreated_contains_recipient_credentials_link_html_and_text()
    {
        var message = UserCreatedEmail.Create(Recipient, Context);

        Assert.Equal(Recipient.Email, message.To);
        Assert.Contains("[DESARROLLO]", message.Subject);
        Assert.Contains("8809100001", message.HtmlBody);
        Assert.Contains("Tu número de documento", message.HtmlBody);
        Assert.Contains("http://127.0.0.1:5173/login", message.HtmlBody);
        Assert.Contains("INGRESAR AL SISTEMA", message.HtmlBody);
        Assert.Contains("8809100001", message.TextBody);
        Assert.Contains("http://127.0.0.1:5173/login", message.TextBody);
    }

    [Fact]
    public void PasswordReset_contains_required_security_content_in_both_bodies()
    {
        var message = PasswordResetEmail.Create(Recipient, Context);

        Assert.Equal(Recipient.Email, message.To);
        Assert.Contains("Restablecimiento", message.Subject);
        Assert.Contains("restableció", message.HtmlBody);
        Assert.Contains("no reconoces", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Contraseña temporal", message.TextBody);
        Assert.Contains("http://127.0.0.1:5173/login", message.TextBody);
    }

    [Fact]
    public void PasswordChanged_contains_timestamp_but_never_password_values()
    {
        const string oldPassword = "OldSecret!42";
        const string newPassword = "NewSecret!42";
        var occurredAt = new DateTimeOffset(2026, 9, 10, 14, 35, 0, TimeSpan.Zero);

        var message = PasswordChangedEmail.Create(Recipient, Context, occurredAt);

        Assert.Equal(Recipient.Email, message.To);
        Assert.Contains("10/09/2026", message.HtmlBody);
        Assert.Contains("14:35 UTC", message.HtmlBody);
        Assert.Contains("no reconoces", message.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(oldPassword, message.HtmlBody + message.TextBody);
        Assert.DoesNotContain(newPassword, message.HtmlBody + message.TextBody);
    }
}
