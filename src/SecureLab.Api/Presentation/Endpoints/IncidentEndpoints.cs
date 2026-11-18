using System.Security.Claims;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents")
            .RequireAuthorization();

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", () => Results.Problem(
                title: "Точку розширення ще не реалізовано",
                detail: "Завершіть цей endpoint під час лабораторної роботи № 1.",
                statusCode: StatusCodes.Status501NotImplemented))
            .WithName("GetIncidentSeveritySummary")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        return endpoints;
    }

    private static async Task<IResult> GetListAsync(
        string? status,
        ClaimsPrincipal user,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        // Ідентичність і роль беруться лише з перевіреного principal
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var currentUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var isAnalyst = user.IsInRole(DbSeeder.AnalystRole);

        IncidentStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
                });
            }

            parsedStatus = candidate;
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, currentUserId, isAnalyst, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        ClaimsPrincipal user,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        // Ідентичність і роль беруться лише з перевіреного principal, а не з запиту
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var currentUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var isAnalyst = user.IsInRole(DbSeeder.AnalystRole);

        var incident = await queries.GetDetailsAsync(id, currentUserId, isAnalyst, cancellationToken);
        return incident is null ? IncidentNotFound() : Results.Ok(incident);
    }

    // Єдина відповідь для чужого і неіснуючого ресурсу: розрізнити їх неможливо
    private static IResult IncidentNotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);
}
