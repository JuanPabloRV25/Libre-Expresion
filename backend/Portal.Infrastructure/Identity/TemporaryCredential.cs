using Microsoft.AspNetCore.Identity;

namespace Portal.Infrastructure.Identity;

internal static class TemporaryCredential
{
    public static void SetDocumentBasedPassword(
        ApplicationUser user,
        string documentNumber,
        IPasswordHasher<ApplicationUser> passwordHasher)
    {
        user.PasswordHash = passwordHasher.HashPassword(user, documentNumber);

        if (passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash!,
                documentNumber) == PasswordVerificationResult.Failed)
        {
            throw new InvalidOperationException(
                "Identity could not verify the temporary credential hash.");
        }
    }
}
