using Microsoft.AspNetCore.Antiforgery;

namespace Portal.Api.Authentication;

public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new ApiErrorResponse(
                "invalid_antiforgery_token",
                "La solicitud no contiene un token antiforgery válido."));
        }

        return await next(context);
    }
}
