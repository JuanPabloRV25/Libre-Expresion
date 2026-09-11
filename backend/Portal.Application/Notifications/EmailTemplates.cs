using System.Globalization;
using System.Net;

namespace Portal.Application.Notifications;

public static class UserCreatedEmail
{
    public static EmailMessage Create(
        NotificationRecipient recipient,
        EmailTemplateContext context,
        string encodedPasswordToken)
    {
        var passwordUrl = EmailTemplateLayout.PasswordResetUrl(recipient, context, encodedPasswordToken);
        var title = "Bienvenido al Portal Libre Expresión";
        var details = $"""
            <p>Tu usuario fue creado correctamente.</p>
            <div class="credential"><strong>Usuario</strong><span>{WebUtility.HtmlEncode(recipient.DocumentNumber)}</span></div>
            <p>Utiliza el enlace personal y temporal de este correo para establecer tu contraseña antes de ingresar.</p>
            <p>El enlace vence en 2 horas. Después ingresarás con este número de documento y la contraseña que establezcas.</p>
            <p><strong>Por seguridad, no compartas este enlace con otras personas.</strong></p>
            """;
        var text = $"""
            Hola, {recipient.FirstName}.

            Tu usuario fue creado correctamente en Portal Libre Expresión.
            Usuario: {recipient.DocumentNumber}

            Utiliza el enlace personal y temporal para establecer tu contraseña antes de ingresar.
            El enlace vence en 2 horas. Después ingresarás con tu número de documento y la contraseña que establezcas.
            No compartas este enlace con otras personas.

            Establecer contraseña: {passwordUrl}
            """;

        return EmailTemplateLayout.Create(
            recipient,
            context,
            "Bienvenido al Portal Libre Expresión",
            title,
            details,
            text,
            "ESTABLECER CONTRASEÑA",
            passwordUrl);
    }
}

public static class PasswordResetEmail
{
    public static EmailMessage Create(
        NotificationRecipient recipient,
        EmailTemplateContext context,
        string encodedPasswordToken)
    {
        var passwordUrl = EmailTemplateLayout.PasswordResetUrl(recipient, context, encodedPasswordToken);
        var title = "Restablece tu contraseña";
        var details = $"""
            <p>Un administrador restableció tu contraseña de acceso.</p>
            <div class="credential"><strong>Usuario</strong><span>{WebUtility.HtmlEncode(recipient.DocumentNumber)}</span></div>
            <p>Utiliza el enlace personal y temporal de este correo para establecer una nueva contraseña.</p>
            <p>El enlace vence en 2 horas y solo puede utilizarse una vez.</p>
            <p><strong>Si tienes dudas sobre esta solicitud, comunícate con el administrador del sistema.</strong></p>
            """;
        var text = $"""
            Hola, {recipient.FirstName}.

            Un administrador solicitó el restablecimiento de tu contraseña de Portal Libre Expresión.
            Usuario: {recipient.DocumentNumber}

            Utiliza el enlace personal y temporal para establecer una nueva contraseña.
            El enlace vence en 2 horas y solo puede utilizarse una vez.
            Si tienes dudas sobre esta solicitud, comunícate con el administrador del sistema.

            Restablecer contraseña: {passwordUrl}
            """;

        return EmailTemplateLayout.Create(
            recipient,
            context,
            "Restablecimiento de contraseña - Portal Libre Expresión",
            title,
            details,
            text,
            "RESTABLECER CONTRASEÑA",
            passwordUrl);
    }
}

public static class PasswordChangedEmail
{
    public static EmailMessage Create(
        NotificationRecipient recipient,
        EmailTemplateContext context,
        DateTimeOffset occurredAt)
    {
        var utc = occurredAt.ToUniversalTime();
        var date = utc.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var time = utc.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var title = "Tu contraseña fue actualizada";
        var details = $"""
            <p>La contraseña de tu cuenta fue actualizada correctamente.</p>
            <div class="credential"><strong>Fecha</strong><span>{date}</span></div>
            <div class="credential"><strong>Hora</strong><span>{time}</span></div>
            <p><strong>Si no reconoces este cambio, comunícate de inmediato con el administrador del sistema.</strong></p>
            <p>Como medida de seguridad, utiliza una contraseña única y evita compartirla.</p>
            """;
        var text = $"""
            Hola, {recipient.FirstName}.

            La contraseña de tu cuenta en Portal Libre Expresión fue actualizada correctamente.
            Fecha: {date}
            Hora: {time}

            Si no reconoces este cambio, comunícate de inmediato con el administrador del sistema.
            Como medida de seguridad, utiliza una contraseña única y evita compartirla.
            """;

        return EmailTemplateLayout.Create(
            recipient,
            context,
            "Confirmación de cambio de contraseña - Portal Libre Expresión",
            title,
            details,
            text);
    }
}

internal static class EmailTemplateLayout
{
    public static EmailMessage Create(
        NotificationRecipient recipient,
        EmailTemplateContext context,
        string subject,
        string title,
        string details,
        string text,
        string? buttonLabel = null,
        string? buttonUrl = null)
    {
        var environment = WebUtility.HtmlEncode(context.EnvironmentLabel.Trim());
        var greeting = WebUtility.HtmlEncode(recipient.FirstName.Trim());
        var encodedButtonUrl = WebUtility.HtmlEncode(buttonUrl ?? LoginUrl(context.PortalBaseUrl));
        var button = buttonLabel is null
            ? string.Empty
            : $"<p><a class=\"button\" href=\"{encodedButtonUrl}\">{WebUtility.HtmlEncode(buttonLabel)}</a></p>";
        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{{WebUtility.HtmlEncode(title)}}</title></head>
            <body style="margin:0;background:#f4f1ef;font-family:Arial,sans-serif;color:#2c2420">
              <style>.wrap{max-width:640px;margin:24px auto;background:#fff;border-radius:12px;overflow:hidden}.header{padding:24px;background:#5b2418;color:#fff}.content{padding:28px;line-height:1.6}.env{display:inline-block;padding:4px 9px;border-radius:999px;background:#f47b20;color:#fff;font-size:12px;font-weight:700}.credential{margin:10px 0;padding:12px;border:1px solid #efd3c0;border-radius:8px;background:#fff8f3}.credential strong,.credential span{display:block}.button{display:inline-block;margin-top:12px;padding:12px 18px;border-radius:8px;background:#f47b20;color:#fff!important;text-decoration:none;font-weight:700}.footer{padding:18px 28px;border-top:1px solid #ece5e1;color:#746761;font-size:12px}</style>
              <div class="wrap">
                <div class="header"><span class="env">{{environment}}</span><h2>Libre Expresión</h2></div>
                <div class="content"><p>Hola, {{greeting}}.</p><h1>{{WebUtility.HtmlEncode(title)}}</h1>{{details}}{{button}}</div>
                <div class="footer">Este es un mensaje automático de Portal Libre Expresión. Por favor no responder.</div>
              </div>
            </body>
            </html>
            """;
        var plainText = $"""
            [{context.EnvironmentLabel.Trim()}] Portal Libre Expresión

            {text.Trim()}

            Este es un mensaje automático de Portal Libre Expresión. Por favor no responder.
            """;

        return new EmailMessage(
            recipient.Email,
            $"[{context.EnvironmentLabel.Trim()}] {subject}",
            html,
            plainText);
    }

    public static string PasswordResetUrl(
        NotificationRecipient recipient,
        EmailTemplateContext context,
        string encodedPasswordToken) =>
        $"{context.PortalBaseUrl.Trim().TrimEnd('/')}/reset-password" +
        $"?userId={Uri.EscapeDataString(recipient.UserId.ToString())}" +
        $"&token={Uri.EscapeDataString(encodedPasswordToken)}";

    public static string LoginUrl(string portalBaseUrl) =>
        $"{portalBaseUrl.Trim().TrimEnd('/')}/login";
}
