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
        var text = $"""
            Hola, {recipient.FirstName}.

            Tu usuario fue creado correctamente en Portal Libre Expresión.
            Usuario: {recipient.DocumentNumber}

            Utiliza este enlace personal y temporal para crear tu contraseña. El enlace vence en 2 horas y solo puede utilizarse una vez.
            Crear mi contraseña: {passwordUrl}

            Si no esperabas este mensaje, comunícate con el administrador del sistema.
            """;

        return EmailTemplateLayout.Create(
            recipient,
            context,
            "Bienvenido al Portal Libre Expresión",
            "Bienvenido al Portal",
            "Tu acceso al Portal Libre Expresión ya está listo. Crea tu contraseña para ingresar por primera vez.",
            "Tu usuario",
            recipient.DocumentNumber,
            ["Vence en 2 horas", "Un solo uso"],
            "Si no esperabas este mensaje, comunícate con el administrador del sistema.",
            text,
            "Crear mi contraseña",
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
        var text = $"""
            Hola, {recipient.FirstName}.

            Un administrador solicitó el restablecimiento de tu contraseña de Portal Libre Expresión.
            Usuario: {recipient.DocumentNumber}

            Utiliza este enlace personal y temporal para establecer una nueva contraseña. El enlace vence en 2 horas y solo puede utilizarse una vez.
            Crear nueva contraseña: {passwordUrl}

            Si no reconoces esta solicitud, comunícate de inmediato con el administrador del sistema.
            """;

        return EmailTemplateLayout.Create(
            recipient,
            context,
            "Restablecimiento de contraseña - Portal Libre Expresión",
            "Restablece tu contraseña",
            "Recibimos una solicitud para restablecer la contraseña de tu cuenta en el Portal Libre Expresión.",
            "Tu usuario",
            recipient.DocumentNumber,
            ["Vence en 2 horas", "Un solo uso"],
            "Si no reconoces esta solicitud, comunícate de inmediato con el administrador del sistema.",
            text,
            "Crear nueva contraseña",
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
        var loginUrl = EmailTemplateLayout.LoginUrl(context.PortalBaseUrl);
        var text = $"""
            Hola, {recipient.FirstName}.

            La contraseña de tu cuenta en Portal Libre Expresión cambió correctamente.
            Estado: Cambio confirmado
            Fecha: {date}
            Hora: {time}

            Ingresar al Portal: {loginUrl}

            Si no reconoces este cambio, comunícate de inmediato con el administrador del sistema.
            """;

        return EmailTemplateLayout.Create(
            recipient,
            context,
            "Confirmación de cambio de contraseña - Portal Libre Expresión",
            "Tu contraseña cambió correctamente",
            "Confirmamos que la contraseña de tu cuenta en el Portal Libre Expresión fue actualizada.",
            "Estado de seguridad",
            "Cambio confirmado",
            [date, time],
            "Si no reconoces este cambio, comunícate de inmediato con el administrador del sistema.",
            text,
            "Ingresar al Portal",
            loginUrl);
    }
}

internal static class EmailTemplateLayout
{
    public static EmailMessage Create(
        NotificationRecipient recipient,
        EmailTemplateContext context,
        string subject,
        string title,
        string introduction,
        string summaryLabel,
        string summaryValue,
        IReadOnlyList<string> badges,
        string warning,
        string text,
        string buttonLabel,
        string buttonUrl)
    {
        var environment = WebUtility.HtmlEncode(context.EnvironmentLabel.Trim());
        var greeting = WebUtility.HtmlEncode(recipient.FirstName.Trim());
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedIntroduction = WebUtility.HtmlEncode(introduction);
        var encodedSummaryLabel = WebUtility.HtmlEncode(summaryLabel);
        var encodedSummaryValue = WebUtility.HtmlEncode(summaryValue);
        var encodedWarning = WebUtility.HtmlEncode(warning);
        var encodedButtonLabel = WebUtility.HtmlEncode(buttonLabel);
        var encodedButtonUrl = WebUtility.HtmlEncode(buttonUrl);
        var badgeHtml = string.Join(
            string.Empty,
            badges.Select(badge =>
                $"<span style=\"display:inline-block;margin:8px 8px 0 0;padding:7px 12px;border-radius:999px;background:#ffffff;border:1px solid #f0d8c9;color:#6d6e71;font-size:12px;font-weight:700;\">{WebUtility.HtmlEncode(badge)}</span>"));

        var html = $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <meta name="color-scheme" content="light only">
              <title>{{encodedTitle}}</title>
              <style>
                @media only screen and (max-width:620px) {
                  .email-shell { width:100% !important; }
                  .email-pad { padding-left:24px !important; padding-right:24px !important; }
                  .header-cell { display:block !important; width:100% !important; text-align:left !important; padding-bottom:10px !important; }
                }
              </style>
            </head>
            <body style="margin:0;padding:0;background:#efedea;color:#3e4044;font-family:'DM Sans',Arial,sans-serif;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">{{encodedIntroduction}}</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;background:#efedea;">
                <tr>
                  <td align="center" style="padding:28px 12px;">
                    <table role="presentation" width="680" cellspacing="0" cellpadding="0" border="0" class="email-shell" style="width:680px;max-width:680px;background:#ffffff;border:1px solid #e8e4e1;border-radius:14px;overflow:hidden;box-shadow:0 8px 24px rgba(31,32,35,.08);">
                      <tr>
                        <td style="background:#2b2c31;border-bottom:4px solid #f26a21;padding:22px 34px;">
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                            <tr>
                              <td class="header-cell" style="vertical-align:middle;">
                                <img src="cid:portal-logo" width="180" alt="Libre Expresión" style="display:block;width:180px;max-width:100%;height:auto;border:0;">
                                <div style="margin-top:7px;color:#d5d5d7;font-size:12px;letter-spacing:.35px;">Portal interno</div>
                              </td>
                              <td class="header-cell" align="right" style="vertical-align:middle;">
                                <span style="display:inline-block;padding:7px 12px;border:1px solid #6d6e71;border-radius:999px;color:#ffffff;font-size:11px;font-weight:700;letter-spacing:.7px;">{{environment}}</span>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td class="email-pad" style="padding:38px 48px 34px;">
                          <p style="margin:0 0 14px;color:#6d6e71;font-size:15px;line-height:1.6;">Hola, {{greeting}}.</p>
                          <h1 style="margin:0 0 18px;color:#1f2023;font-family:Manrope,Arial,sans-serif;font-size:30px;line-height:1.22;font-weight:800;letter-spacing:-.4px;">{{encodedTitle}}</h1>
                          <p style="margin:0 0 25px;color:#54565a;font-size:15px;line-height:1.7;">{{encodedIntroduction}}</p>

                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 24px;background:#fdf6f2;border-radius:10px;">
                            <tr>
                              <td width="5" style="width:5px;background:#f26a21;border-radius:10px 0 0 10px;font-size:0;">&nbsp;</td>
                              <td style="padding:19px 20px;">
                                <div style="color:#8a4b2a;font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.8px;">{{encodedSummaryLabel}}</div>
                                <div style="margin-top:5px;color:#2b2c31;font-family:Manrope,Arial,sans-serif;font-size:18px;line-height:1.35;font-weight:800;">{{encodedSummaryValue}}</div>
                                <div>{{badgeHtml}}</div>
                              </td>
                            </tr>
                          </table>

                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 22px;">
                            <tr>
                              <td align="center" bgcolor="#f26a21" style="border-radius:8px;">
                                <a href="{{encodedButtonUrl}}" style="display:block;padding:15px 22px;color:#ffffff;text-decoration:none;font-family:Manrope,Arial,sans-serif;font-size:15px;font-weight:800;">{{encodedButtonLabel}}</a>
                              </td>
                            </tr>
                          </table>

                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="margin-top:24px;">
                            <tr>
                              <td width="30" valign="top" style="width:30px;color:#c97e06;font-size:20px;line-height:1;">&#9650;</td>
                              <td style="color:#6d6e71;font-size:13px;line-height:1.55;"><strong style="color:#54565a;">Importante:</strong> {{encodedWarning}}</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td class="email-pad" style="padding:22px 48px;background:#f5f3f1;border-top:1px solid #e8e4e1;">
                          <img src="cid:portal-logo" width="140" alt="Libre Expresión" style="display:block;width:140px;max-width:100%;height:auto;border:0;margin-bottom:10px;">
                          <p style="margin:0;color:#77787c;font-size:11px;line-height:1.6;">Este es un mensaje automático de Portal Libre Expresión. Por favor no responder.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
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
