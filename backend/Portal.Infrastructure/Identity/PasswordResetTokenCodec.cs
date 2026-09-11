using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Portal.Infrastructure.Identity;

internal static class PasswordResetTokenCodec
{
    public static string Encode(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static bool TryDecode(string encodedToken, out string token)
    {
        token = string.Empty;

        if (string.IsNullOrWhiteSpace(encodedToken))
        {
            return false;
        }

        try
        {
            token = Encoding.UTF8.GetString(
                WebEncoders.Base64UrlDecode(encodedToken));
            return token.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
