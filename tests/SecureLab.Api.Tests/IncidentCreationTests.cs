using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class IncidentCreationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    // Пароль береться лише зі змінної середовища, не з коду
    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Post_InvalidDto_Returns400()
    {
        using var client = await factory.LoginAsync("alice", Password);
        await client.UseAntiforgeryTokenAsync();

        var body = new { title = "", description = "", severity = "7", occurredAtUtc = (DateTimeOffset?)null };
        using var response = await client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // 400 саме від validation, а не від відмови antiforgery
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("severity", out _));
    }

    [Fact]
    public async Task Post_DuplicateTitle_Returns409()
    {
        using var client = await factory.LoginAsync("alice", Password);
        await client.UseAntiforgeryTokenAsync();

        var title = $"Regression-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "Коректний опис для регресійного тесту T-03.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        using var first = await client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(i => i.Title == title).ExecuteDeleteAsync();
    }
}
