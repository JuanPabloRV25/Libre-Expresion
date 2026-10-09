using System.Net;
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
    private const string EncodedToken = "encoded-token_123";

    private static readonly EmailTemplateContext Context = new(
        "http://127.0.0.1:5173/",
        "DESARROLLO");

    [Fact]
    public void UserCreated_contains_password_setup_link_but_no_temporary_password()
    {
        var message = UserCreatedEmail.Create(Recipient, Context, EncodedToken);

        Assert.Equal(Recipient.Email, message.To);
        Assert.Contains("[DESARROLLO]", message.Subject);
        Assert.Contains("8809100001", message.HtmlBody);
        Assert.Contains("/reset-password?userId=", message.HtmlBody);
        Assert.Contains("token=encoded-token_123", message.HtmlBody);
        Assert.Contains("Crear mi contraseña", WebUtility.HtmlDecode(message.HtmlBody));
        Assert.Contains("cid:portal-logo", message.HtmlBody);
        Assert.Contains("font-family:Manrope", message.HtmlBody);
        Assert.Contains("'DM Sans'", message.HtmlBody);
        Assert.Contains("Vence en 2 horas", message.HtmlBody);
        Assert.Contains("8809100001", message.TextBody);
        Assert.Contains("token=encoded-token_123", message.TextBody);
        Assert.DoesNotContain("Contraseña temporal", message.HtmlBody + message.TextBody);
        Assert.DoesNotContain("Tu número de documento", message.HtmlBody + message.TextBody);
    }

    [Fact]
    public void PasswordReset_contains_required_security_content_in_both_bodies()
    {
        var message = PasswordResetEmail.Create(Recipient, Context, EncodedToken);

        Assert.Equal(Recipient.Email, message.To);
        Assert.Contains("Restablecimiento", message.Subject);
        Assert.Contains("restablecer", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Crear nueva contraseña", WebUtility.HtmlDecode(message.HtmlBody));
        Assert.Contains("Vence en 2 horas", message.HtmlBody);
        Assert.Contains("Un solo uso", message.HtmlBody);
        Assert.Contains("cid:portal-logo", message.HtmlBody);
        Assert.DoesNotContain("Si el botón no funciona", message.HtmlBody);
        Assert.Contains("token=encoded-token_123", message.HtmlBody);
        Assert.Contains("token=encoded-token_123", message.TextBody);
        Assert.DoesNotContain("Contraseña temporal", message.HtmlBody + message.TextBody);
        Assert.DoesNotContain("Tu número de documento", message.HtmlBody + message.TextBody);
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
        Assert.Contains("Cambio confirmado", message.HtmlBody);
        Assert.Contains("Ingresar al Portal", message.HtmlBody);
        Assert.Contains("/login", message.HtmlBody);
        Assert.Contains("no reconoces", message.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(oldPassword, message.HtmlBody + message.TextBody);
        Assert.DoesNotContain(newPassword, message.HtmlBody + message.TextBody);
    }
}
