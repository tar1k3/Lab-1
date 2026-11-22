using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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

    // ЛР 6: S-02 з ЛР 2, раніше лише ручна перевірка
    [Fact]
    public async Task S02_SqlPayload_IsTreatedAsText_AndSortByIsAllowlisted()
    {
        using var host = factory.WithWebHostBuilder(_ => { });   // окремий лічильник rate limit
        using var client = await host.LoginAsync("alice", Password);

        // Позитив: порожній пошук повертає seed-інциденти
        var all = await client.GetFromJsonAsync<List<JsonElement>>("/api/incidents/search?q=");
        Assert.NotNull(all);
        Assert.NotEmpty(all);

        // Негатив: класичний payload не повертає «всі рядки» — він шукається як буквальний текст
        var payload = Uri.EscapeDataString("' OR '1'='1");
        var injected = await client.GetFromJsonAsync<List<JsonElement>>($"/api/incidents/search?q={payload}");
        Assert.NotNull(injected);
        Assert.Empty(injected);

        // Негатив: довільний sortBy не потрапляє в ORDER BY — 400 від allowlist
        var sort = Uri.EscapeDataString("title; DROP TABLE incidents");
        using var badSort = await client.GetAsync($"/api/incidents/search?sortBy={sort}");
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
    }
}
