using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Portal.Infrastructure.Identity;

public sealed class PortalCookieAuthenticationEvents(
    UserManager<ApplicationUser> userManager) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var user = principal is null
            ? null
            : await userManager.GetUserAsync(principal);

        if (user is null || !user.IsActive)
        {
            await RejectAsync(context);
            return;
        }

        if (!userManager.SupportsUserSecurityStamp)
        {
            return;
        }

        var claimType = userManager.Options.ClaimsIdentity.SecurityStampClaimType;
        var principalStamp = principal?.FindFirstValue(claimType);
        var currentStamp = await userManager.GetSecurityStampAsync(user);

        if (!string.Equals(principalStamp, currentStamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
        }
    }

    public override Task RedirectToLogin(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private static async Task RejectAsync(
        CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }
}
