using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;
using SecureLab.Api.Security;

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

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteIncident")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AddEndpointFilter<AntiforgeryFilter>();

        group.MapGet("/severity-summary", () => Results.Problem(
                title: "Точку розширення ще не реалізовано",
                detail: "Завершіть цей endpoint під час лабораторної роботи № 1.",
                statusCode: StatusCodes.Status501NotImplemented))
            .WithName("GetIncidentSeveritySummary")
            .ProducesProblem(StatusCodes.Status501NotImplemented);

        // Дочірній ресурс: окрема група, теж лише для автентифікованих
        var comments = endpoints.MapGroup("/api/incident-comments")
            .WithTags("IncidentComments")
            .RequireAuthorization();

        comments.MapGet("/{id:guid}", GetCommentAsync)
            .WithName("GetIncidentComment")
            .Produces<IncidentCommentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    // ЛР 4: рішення авторизації для DELETE ухвалюється до видалення
    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        SecureLabDbContext db,
        CancellationToken cancellationToken)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var currentUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var isAnalyst = user.IsInRole(DbSeeder.AnalystRole);

        var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        // Неіснуючий і чужий ресурс для не-Analyst: однакова відповідь 404
        if (incident is null || (incident.OwnerUserId != currentUserId && !isAnalyst))
        {
            return IncidentNotFound();
        }

        // Analyst бачить чужий інцидент, але видаляти не може: 403
        if (incident.OwnerUserId != currentUserId)
        {
            return Results.Problem(
                title: "Дію заборонено",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Доступ є, але стан ресурсу не дозволяє дію: 409
        if (incident.Status != IncidentStatus.New)
        {
            return Results.Problem(
                title: "Видалити можна лише інцидент у статусі New",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.Incidents.Remove(incident);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    // ЛР 4: доступ до коментаря через право на батьківський інцидент
    private static async Task<IResult> GetCommentAsync(
        Guid id,
        ClaimsPrincipal user,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var currentUserId))
        {
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        var isAnalyst = user.IsInRole(DbSeeder.AnalystRole);

        var comment = await queries.GetCommentAsync(id, currentUserId, isAnalyst, cancellationToken);

        // Чужий, internal для Reporter і неіснуючий коментар: однаковий 404
        return comment is null
            ? Results.Problem(
                title: "Коментар не знайдено",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(comment);
    }

    // Єдина відповідь для чужого і неіснуючого ресурсу: розрізнити їх неможливо
    private static IResult IncidentNotFound() => Results.Problem(
        title: "Інцидент не знайдено",
        statusCode: StatusCodes.Status404NotFound);
}
