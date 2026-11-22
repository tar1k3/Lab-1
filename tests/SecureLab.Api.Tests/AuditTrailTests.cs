using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

// ЛР 6: REG-AUDIT-01 — дозволений triage залишає рівно один audit-запис, відмова — жодного
public sealed class AuditTrailTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private sealed record CreatedDto(Guid Id);

    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private async Task<List<IncidentStatusHistory>> HistoryAsync(Guid incidentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.IncidentStatusHistory.AsNoTracking()
            .Where(h => h.IncidentId == incidentId).ToListAsync();
    }

    private async Task<IncidentStatus> StatusAsync(Guid incidentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking()
            .Where(i => i.Id == incidentId).Select(i => i.Status).SingleAsync();
    }

    [Fact]
    public async Task RegAudit01_DeniedTriage_NoRecord_AllowedTriage_ExactlyOneRecord()
    {
        // Новий інцидент Alice у статусі New
        using var alice = await factory.LoginAsync("alice", Password);
        await alice.UseAntiforgeryTokenAsync();
        using var created = await alice.PostAsJsonAsync("/api/incidents", new
        {
            title = $"AUDIT-{Guid.NewGuid():N}",
            description = "REG-AUDIT-01",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        // Reporter -> 403: статус лишається New, записів в історії немає
        using var denied = await alice.PostAsync($"/api/incidents/{id}/triage", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(IncidentStatus.New, await StatusAsync(id));
        Assert.Empty(await HistoryAsync(id));

        // Analyst -> 200: рівно один запис з усіма полями аудиту
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        using var morgan = await factory.LoginAsync("morgan", Password);
        await morgan.UseAntiforgeryTokenAsync();
        using var allowed = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        var record = Assert.Single(await HistoryAsync(id));
        Assert.Equal(DbSeeder.MorganId, record.ChangedByUserId);          // subject
        Assert.Equal("incident.triage: allowed", record.Note);           // action + result
        Assert.Equal(id, record.IncidentId);                              // resource
        Assert.Equal(IncidentStatus.New, record.OldStatus);
        Assert.Equal(IncidentStatus.Triaged, record.NewStatus);
        Assert.InRange(record.CreatedAtUtc, before, DateTimeOffset.UtcNow.AddSeconds(5)); // time
    }
}
