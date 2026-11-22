using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Security;
using System.Security.Claims;
using System.Diagnostics;
namespace SecureLab.Api.Scaffolding;

public static class Lab02Endpoints
{
      private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("_", "\\_").Replace("%", "\\%");
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapPost("/api/incidents", async (
          CreateIncidentRequest request,
          HttpContext httpContext,
          SecureLabDbContext db,
          CancellationToken ct) =>
      {
    // Owner береться лише з перевіреного principal, а не з body чи header
            var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var ownerUserId))
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Заголовок обов'язковий."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Заголовок не довший за 160 символів."];

            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Опис не довший за 4000 символів."];

            var severityIsValid =
                Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["Час настання обов'язковий."];
            else if (request.OccurredAtUtc > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Час настання не може випереджати поточний момент більш ніж на 5 хвилин."];

            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            if (severityIsValid
                && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && !errors.ContainsKey("description")
                && description.Length < 40)
                errors["description"] = ["Для High або Critical опис має містити щонайменше 40 символів."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            var exists = await db.Incidents.AnyAsync(i =>
                i.Title == title &&
                (i.Status == IncidentStatus.New || i.Status == IncidentStatus.Triaged ||
                 i.Status == IncidentStatus.InProgress || i.Status == IncidentStatus.Resolved), ct);
            if (exists)
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Інцидент з таким заголовком вже існує",
                    detail: "Активний інцидент з таким title уже зареєстровано.");

            var occurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime();

            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                Title = title,
                Description = description,
                Severity = severity,
                OccurredAtUtc = occurredAtUtc,
                Status = IncidentStatus.New,
                OwnerUserId = ownerUserId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            var response = new CreatedIncidentResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.UpdatedAtUtc);

            return Results.Created($"/api/incidents/{incident.Id}", response);
        }).RequireAuthorization()
          .AddEndpointFilter<AntiforgeryFilter>();
        // ВИПРАВЛЕНО (ЛР 4): рішення авторизації ухвалюється до будь-якої зміни
        app.MapPut("/api/incidents/{id:guid}", async (
            Guid id,
            UpdateIncidentRequest request,
            ClaimsPrincipal user,
            SecureLabDbContext db,
            CancellationToken ct) =>
        {
            // Ідентичність і роль беруться лише з перевіреного principal
            var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

            var isAnalyst = user.IsInRole(DbSeeder.AnalystRole);

            var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == id, ct);

            // Неіснуючий і чужий ресурс для не-Analyst дають однакову відповідь 404
            if (incident is null || (incident.OwnerUserId != currentUserId && !isAnalyst))
                return Results.Problem(
                    title: "Інцидент не знайдено",
                    statusCode: StatusCodes.Status404NotFound);

            // Analyst бачить чужий інцидент, але змінювати його не може: 403
            if (incident.OwnerUserId != currentUserId)
                return Results.Problem(
                    title: "Дію заборонено",
                    statusCode: StatusCodes.Status403Forbidden);

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Заголовок обов'язковий."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Заголовок не довший за 160 символів."];
            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Опис не довший за 4000 символів."];
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            // Змінюються лише поля DTO; власник, статус і severity задає лише сервер
            incident.Title = request.Title.Trim();
            incident.Description = request.Description.Trim();
            incident.OccurredAtUtc = request.OccurredAtUtc.ToUniversalTime();
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization();
        // ВРАЗЛИВО (ЛР 4): лише вхід, роль не перевіряється, статус береться з body
                // ВИПРАВЛЕНО (ЛР 4): роль Analyst, перехід лише з New, наступний статус визначає сервер
        app.MapPost("/api/incidents/{id:guid}/triage", async (
            Guid id,
            ClaimsPrincipal user,
            HttpContext httpContext,
            ILoggerFactory loggerFactory,
            SecureLabDbContext db,
            CancellationToken ct) =>
        {
            // Ідентичність і роль беруться лише з перевіреного principal, тіла запиту немає
            var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var currentUserId))
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);

            var incident = await db.Incidents.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (incident is null)
                return Results.Problem(
                    title: "Інцидент не знайдено",
                    statusCode: StatusCodes.Status404NotFound);

            // Privileged action: лише Analyst; власник без ролі права не має
            if (!user.IsInRole(DbSeeder.AnalystRole))
            {
                var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
                loggerFactory.CreateLogger("SecureLab.Audit").LogWarning(
                    "Audit authorization.denied: user {UserId}, action {Action}, incident {IncidentId}, result {Result}, at {At}, trace {TraceId}",
                    currentUserId, "incident.triage", incident.Id, "denied", DateTimeOffset.UtcNow, traceId);

                return Results.Problem(
                    title: "Дію заборонено",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // Перехід дозволено лише зі статусу New
            if (incident.Status != IncidentStatus.New)
                return Results.Problem(
                    title: "Triage можливий лише для інциденту у статусі New",
                    statusCode: StatusCodes.Status409Conflict);

            // Наступний статус визначає сервер, а не клієнт
            incident.Status = IncidentStatus.Triaged;
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { incident.Id, Status = incident.Status.ToString() });
        }).RequireAuthorization();
        app.MapGet("/api/incidents/search", async (
    string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
{
    var pattern = "%" + EscapeLike(q ?? "") + "%";

    var found = db.Incidents.AsNoTracking()
        .Where(i => EF.Functions.ILike(i.Title, pattern, "\\")
                 || EF.Functions.ILike(i.Description, pattern, "\\"));

    IQueryable<Incident>? ordered = sortBy switch
    {
        null or "" or "createdAtUtc" => found
            .OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id),
        "severity" => found
            .OrderBy(i => i.Severity == IncidentSeverity.Critical ? 0
                : i.Severity == IncidentSeverity.High ? 1
                : i.Severity == IncidentSeverity.Medium ? 2 : 3)
            .ThenBy(i => i.Id),
        "status" => found
            .OrderBy(i => i.Status == IncidentStatus.New ? 0
                : i.Status == IncidentStatus.Triaged ? 1
                : i.Status == IncidentStatus.InProgress ? 2
                : i.Status == IncidentStatus.Resolved ? 3 : 4)
            .ThenBy(i => i.Id),
        _ => null
    };

    if (ordered is null)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
        });

    var items = await ordered
        .Take(50)
        .Select(i => new IncidentListItemResponse(
            i.Id, i.Title, i.Severity.ToString(), i.Status.ToString(),
            i.OccurredAtUtc, i.CreatedAtUtc))
        .ToListAsync(ct);

    return Results.Ok(items);
});
    }
}

public sealed record CreateIncidentRequest(
    string? Title,
    string? Description,
    string? Severity,
    DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id,
    string Title,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record IncidentListItemResponse(
    Guid Id,
    string Title,
    string Severity,
    string Status,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record UpdateIncidentRequest(
    string Title,
    string Description,
    DateTimeOffset OccurredAtUtc);

