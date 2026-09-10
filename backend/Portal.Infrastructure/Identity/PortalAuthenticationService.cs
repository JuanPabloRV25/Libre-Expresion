using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portal.Application.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Identity;

public sealed class PortalAuthenticationService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext dbContext,
    TimeProvider timeProvider) : IPortalAuthenticationService
{
    public async Task<PortalLoginResult> LoginAsync(
        string documentNumber,
        string password)
    {
        if (string.IsNullOrWhiteSpace(documentNumber)
            || string.IsNullOrEmpty(password))
        {
            return new PortalLoginResult(PortalLoginStatus.InvalidCredentials);
        }

        var user = await userManager.FindByNameAsync(documentNumber.Trim());

        if (user is null)
        {
            return new PortalLoginResult(PortalLoginStatus.InvalidCredentials);
        }

        var passwordCheck = await signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);

        if (passwordCheck.IsLockedOut)
        {
            return new PortalLoginResult(PortalLoginStatus.LockedOut);
        }

        if (!passwordCheck.Succeeded)
        {
            return new PortalLoginResult(PortalLoginStatus.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return new PortalLoginResult(PortalLoginStatus.Inactive);
        }

        await signInManager.SignInAsync(user, isPersistent: false);

        return new PortalLoginResult(
            PortalLoginStatus.Succeeded,
            user.MustChangePassword);
    }

    public async Task<PasswordChangeResult> ChangeRequiredPasswordAsync(
        Guid userId,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return new PasswordChangeResult(PasswordChangeStatus.ConfirmationMismatch);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return new PasswordChangeResult(PasswordChangeStatus.UserNotFound);
        }

        if (!user.IsActive)
        {
            return new PasswordChangeResult(PasswordChangeStatus.Inactive);
        }

        if (!user.MustChangePassword)
        {
            return new PasswordChangeResult(PasswordChangeStatus.ChangeNotRequired);
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetResult = await userManager.ResetPasswordAsync(
            user,
            resetToken,
            newPassword);

        if (!resetResult.Succeeded)
        {
            return new PasswordChangeResult(PasswordChangeStatus.InvalidPassword);
        }

        user.MustChangePassword = false;
        user.UpdatedAt = timeProvider.GetUtcNow();
        EnsureSucceeded(
            await userManager.UpdateAsync(user),
            "update the mandatory-password-change state");

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await signInManager.SignOutAsync();

        return new PasswordChangeResult(PasswordChangeStatus.Succeeded);
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return new PasswordChangeResult(PasswordChangeStatus.ConfirmationMismatch);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());

        if (user is null)
        {
            return new PasswordChangeResult(PasswordChangeStatus.UserNotFound);
        }

        if (!user.IsActive)
        {
            return new PasswordChangeResult(PasswordChangeStatus.Inactive);
        }

        if (user.MustChangePassword)
        {
            return new PasswordChangeResult(PasswordChangeStatus.ChangeRequired);
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var changeResult = await userManager.ChangePasswordAsync(
            user,
            currentPassword,
            newPassword);

        if (!changeResult.Succeeded)
        {
            var currentPasswordIsInvalid = changeResult.Errors.Any(
                error => error.Code == "PasswordMismatch");

            return new PasswordChangeResult(
                currentPasswordIsInvalid
                    ? PasswordChangeStatus.InvalidCurrentPassword
                    : PasswordChangeStatus.InvalidPassword);
        }

        user.UpdatedAt = timeProvider.GetUtcNow();
        EnsureSucceeded(
            await userManager.UpdateAsync(user),
            "update the password-change timestamp");

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await signInManager.RefreshSignInAsync(user);

        return new PasswordChangeResult(PasswordChangeStatus.Succeeded);
    }

    public Task SignOutAsync() => signInManager.SignOutAsync();

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errorCodes = string.Join(
            ", ",
            result.Errors.Select(error => error.Code).Distinct(StringComparer.Ordinal));

        throw new InvalidOperationException(
            $"Identity failed to {operation}. Error codes: {errorCodes}.");
    }
}
