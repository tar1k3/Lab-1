using Microsoft.AspNetCore.Antiforgery;

namespace SecureLab.Api.Security;

// ЛР 6: єдине місце перевірки antiforgery-токена для всіх cookie-автентифікованих змінювальних маршрутів.
// ValidateRequestAsync перевіряє пару: cookie securelab-af і request token у header X-CSRF-TOKEN,
// прив'язаний до поточного principal. Якщо токена немає або він невалідний — 400, handler не виконується.
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                title: "Antiforgery token відсутній або недійсний",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}
