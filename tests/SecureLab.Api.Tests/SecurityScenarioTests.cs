using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

// Безпекові сценарії ЛР3: A-01 … A-07
public class SecurityScenarioTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    // Клієнт без входу; не йде за redirect, щоб побачити справжній статус
    private HttpClient CreateAnonymousClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    [Fact]
    public async Task A01_AnonymousCreate_Returns401NotRedirect()
    {
        using var client = CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = $"A01-{Guid.NewGuid():N}",
            description = "Анонімний запит",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);                    // не 302 redirect
        Assert.Empty(response.Headers.WwwAuthenticate);             // без Bearer challenge
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
