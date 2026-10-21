using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        var query = dbContext.Incidents.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<IncidentDetailsResponse?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    // НОВИЙ МЕТОД ДЛЯ КРОКУ 2:
    public async Task<IncidentSeveritySummaryResponse> GetSeveritySummaryAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Отримання статистики інцидентів за критичністю");

        // 1. Формуємо запит до БД з групуванням
        var items = await dbContext.Incidents
            .AsNoTracking()
            .GroupBy(i => i.Severity)
            .Select(group => new IncidentSeveritySummaryItem(
                group.Key.ToString(), 
                group.Count()))
            .ToListAsync(cancellationToken);

        // 2. Сортуємо результати у пам'яті (за спаданням кількості, потім за алфавітом)
        var sortedItems = items
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Severity)
            .ToList();

        // 3. Структуроване логування кожної групи
        foreach (var item in sortedItems)
        {
            logger.LogInformation("Критичність {Severity}: {Count} інцидент(ів)", item.Severity, item.Count);
        }

        return new IncidentSeveritySummaryResponse(sortedItems);
    }
}
