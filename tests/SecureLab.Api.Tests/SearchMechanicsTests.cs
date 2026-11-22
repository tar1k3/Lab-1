using System.Net;

namespace SecureLab.Api.Tests;

public sealed class SearchMechanicsTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Search_ReturnsJson()
    {
        using var client = await factory.LoginAsync("alice", Password);
        using var response = await client.GetAsync("/api/incidents/search?q=навчальн");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    // ЛР 6 (F-03): пошук без входу більше не розкриває інциденти
    [Fact]
    public async Task Search_Anonymous_Returns401()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/api/incidents/search?q=");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
