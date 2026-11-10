using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class IncidentCreationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_InvalidDto_Returns400()
    {
        var body = new { title = "", description = "", severity = "7", occurredAtUtc = (DateTimeOffset?)null };
        using var response = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_DuplicateTitle_Returns409()
    {
        var title = $"Regression-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "Коректний опис для регресійного тесту T-03.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        using var first = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(i => i.Title == title).ExecuteDeleteAsync();
    }
}
