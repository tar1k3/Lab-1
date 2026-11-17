
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;
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
    // DTO для читання відповіді /api/me у тестах
private sealed record MeDto(Guid Id, string UserName, string Role);

private static readonly Guid AliceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
private static readonly Guid BobId = Guid.Parse("10000000-0000-0000-0000-000000000002");

[Fact]
public async Task A02_LoginThenMe_ReturnsCurrentUser()
{
    using var client = await factory.LoginAsync("alice", Password);

    using var response = await client.GetAsync("/api/me");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var me = await response.Content.ReadFromJsonAsync<MeDto>();
    Assert.NotNull(me);
    Assert.Equal(AliceId, me.Id);
    Assert.Equal("alice", me.UserName);
}
[Fact]
public async Task A03_MeWithForeignDemoHeader_StaysCurrentUser()
{
    using var client = await factory.LoginAsync("alice", Password);

    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
    request.Headers.Add("X-Demo-UserId", BobId.ToString());

    using var response = await client.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var me = await response.Content.ReadFromJsonAsync<MeDto>();
    Assert.NotNull(me);
    Assert.Equal(AliceId, me.Id);   // не Bob
}
[Fact]
public async Task A04_CreateWithoutOwner_OwnerFromPrincipal()
{
    using var client = await factory.LoginAsync("bob", Password);

    using var response = await client.PostAsJsonAsync("/api/incidents", new
    {
        title = $"A04-{Guid.NewGuid():N}",   // унікальний title
        description = "Створено Bob без поля owner",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
    });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var created = await response.Content.ReadFromJsonAsync<CreatedDto>();
    Assert.NotNull(created);
    Assert.Equal(BobId, await GetOwnerIdAsync(created.Id));
}
private sealed record CreatedDto(Guid Id);

private async Task<Guid> GetOwnerIdAsync(Guid incidentId)
{
    await using var scope = factory.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
    var incident = await db.Incidents.AsNoTracking().SingleAsync(i => i.Id == incidentId);
    return incident.OwnerUserId;
}
[Fact]
public async Task A05_CreateWithForeignOwner_OwnerStaysPrincipal()
{
    using var client = await factory.LoginAsync("bob", Password);

    using var response = await client.PostAsJsonAsync("/api/incidents", new
    {
        title = $"A05-{Guid.NewGuid():N}",
        description = "Bob намагається підставити власника Alice",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        ownerUserId = AliceId   // чужий owner у JSON
    });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var created = await response.Content.ReadFromJsonAsync<CreatedDto>();
    Assert.NotNull(created);
    Assert.Equal(BobId, await GetOwnerIdAsync(created.Id));   // не Alice
}
[Fact]
public async Task A06_Logout_ThenProtectedRequestReturns401()
{
    using var client = await factory.LoginAsync("alice", Password);

    // До logout сеанс активний
    using var before = await client.GetAsync("/api/me");
    Assert.Equal(HttpStatusCode.OK, before.StatusCode);

    // Logout тим самим клієнтом
    using var logout = await client.PostAsync("/api/auth/logout", null);
    Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

    // Після logout той самий клієнт отримує 401
    using var after = await client.GetAsync("/api/me");
    Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
}
private async Task<string> LoginFailureShapeAsync(string userName, string password)
{
    using var client = CreateAnonymousClient();
    using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });

    var body = await response.Content.ReadAsStringAsync();
    using var json = JsonDocument.Parse(body);
    var root = json.RootElement;

    string Field(string name) =>
        root.TryGetProperty(name, out var value) ? value.ToString() : "<none>";

    return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType?.MediaType}|" +
           $"{Field("type")}|{Field("title")}|{Field("status")}|{Field("detail")}";
}

[Fact]
public async Task A07_UnknownUserAndWrongPassword_SameResult()
{
    var unknownUser = await LoginFailureShapeAsync($"nobody-{Guid.NewGuid():N}", Password);
    var wrongPassword = await LoginFailureShapeAsync("alice", Password + "-wrong");

    Assert.StartsWith("401", unknownUser);
    Assert.Equal(unknownUser, wrongPassword);
}
[Fact]
public async Task CookieFlags_LoginOverHttps_SetsSecureAttributes()
{
    using var client = CreateAnonymousClient();

    using var response = await client.PostAsJsonAsync("/api/auth/login",
        new { userName = "alice", password = Password });

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

    const string cookieName = ".AspNetCore.Identity.Application=";
    var matching = response.Headers.GetValues("Set-Cookie")
        .Where(value => value.StartsWith(cookieName))
        .ToArray();
    Assert.True(matching.Length == 1);

    var attributes = matching[0].Split(';').Skip(1)
        .Select(part => part.Trim().ToLowerInvariant())
        .ToArray();

    Assert.Contains("httponly", attributes);
    Assert.Contains("secure", attributes);
    Assert.Contains("samesite=lax", attributes);
    Assert.Contains("path=/", attributes);
    Assert.DoesNotContain(attributes, a => a.StartsWith("expires="));
}
}
