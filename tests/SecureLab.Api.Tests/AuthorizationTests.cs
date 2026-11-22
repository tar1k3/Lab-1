using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

// Один вхід на користувача для всього класу: так тести не впираються в rate limit login
public sealed class AuthorizationFixture : IAsyncLifetime
{
    public SecureLabApiFactory Factory { get; } = new();
    public HttpClient Alice { get; private set; } = null!;
    public HttpClient Bob { get; private set; } = null!;
    public HttpClient Morgan { get; private set; } = null!;

    // Пароль лише зі змінної середовища, у Git його немає
    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    public async Task InitializeAsync()
    {
        Alice = await Factory.LoginAsync("alice", Password);
        Bob = await Factory.LoginAsync("bob", Password);
        Morgan = await Factory.LoginAsync("morgan", Password);

        // ЛР 6: кожен cookie-клієнт отримує власний antiforgery-токен для змінювальних запитів
        await Alice.UseAntiforgeryTokenAsync();
        await Bob.UseAntiforgeryTokenAsync();
        await Morgan.UseAntiforgeryTokenAsync();
    }

    public async Task DisposeAsync()
    {
        Alice.Dispose();
        Bob.Dispose();
        Morgan.Dispose();
        await Factory.DisposeAsync();
    }
}

public sealed class AuthorizationTests(AuthorizationFixture fx) : IClassFixture<AuthorizationFixture>
{
    private static readonly Guid BobCommentId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid InternalCommentId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task A02_Owner_ReadsOwnIncident_200()
    {
        var incident = await fx.Alice.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.NotNull(incident);
        Assert.Equal(DbSeeder.AliceIncidentId, incident.Id);
        Assert.Equal("Аліса Коваль", incident.OwnerDisplayName);
    }

    [Fact]
    public async Task A03_A10_ForeignAndUnknownId_ReturnSame404()
    {
        using var foreign = await fx.Alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        using var unknown = await fx.Alice.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        var foreignBody = await foreign.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(foreign.StatusCode, unknown.StatusCode);
        Assert.Equal(
            foreign.Content.Headers.ContentType?.MediaType,
            unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Title(foreignBody), Title(unknownBody));
        Assert.DoesNotContain("Боб Мельник", foreignBody);
        Assert.DoesNotContain(DbSeeder.BobIncidentId.ToString(), foreignBody);
    }

    [Fact]
    public async Task A03R_Analyst_ReadsForeignIncident_200()
    {
        using var response = await fx.Morgan.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A04_Reporter_CannotUpdateForeignIncident_404_AndStateUnchanged()
    {
        var before = await fx.Bob.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.BobIncidentId}");

        using var response = await fx.Alice.PutAsJsonAsync(
            $"/api/incidents/{DbSeeder.BobIncidentId}",
            new { title = "Changed by Alice", description = "Attempt to modify a foreign incident.", occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1) });

        var after = await fx.Bob.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before!.Title, after!.Title);
        Assert.Equal(before.Description, after.Description);
    }

    [Fact]
    public async Task A05_Reporter_ListContainsOnlyOwnIncidents()
    {
        var incidents = await fx.Alice.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.DoesNotContain(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task A06_Reporter_CannotTriage_EvenWithForgedFields_403_StatusStaysNew()
    {
        var id = await CreateNewIncidentAsAliceAsync();

        using var response = await fx.Alice.PostAsJsonAsync(
            $"/api/incidents/{id}/triage",
            new { status = "Closed", role = "Analyst", ownerUserId = DbSeeder.MorganId, isTransitionAllowed = true });

        var incident = await fx.Alice.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("New", incident!.Status);
    }

    [Fact]
    public async Task A07_Analyst_TriagesNewIncident_200_RepeatReturns409()
    {
        var id = await CreateNewIncidentAsAliceAsync();

        using var first = await fx.Morgan.PostAsync($"/api/incidents/{id}/triage", null);
        using var second = await fx.Morgan.PostAsync($"/api/incidents/{id}/triage", null);
        var incident = await fx.Morgan.GetFromJsonAsync<IncidentDetailsResponse>($"/api/incidents/{id}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Triaged", incident!.Status);
    }

    [Fact]
    public async Task A12_ChildComment_ForeignAndInternal_404_AnalystReadsInternal()
    {
        using var foreignComment = await fx.Alice.GetAsync($"/api/incident-comments/{BobCommentId}");
        using var internalForReporter = await fx.Alice.GetAsync($"/api/incident-comments/{InternalCommentId}");
        using var internalForAnalyst = await fx.Morgan.GetAsync($"/api/incident-comments/{InternalCommentId}");
        var aliceDetails = await fx.Alice.GetFromJsonAsync<IncidentDetailsResponse>(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, foreignComment.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, internalForReporter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, internalForAnalyst.StatusCode);
        Assert.DoesNotContain(aliceDetails!.Comments, comment => comment.Id == InternalCommentId);
    }

    // Для triage щоразу новий інцидент у статусі New, щоб тести не залежали один від одного
    private async Task<Guid> CreateNewIncidentAsAliceAsync()
    {
        using var response = await fx.Alice.PostAsJsonAsync("/api/incidents", new
        {
            title = $"Authorization test {Guid.NewGuid():N}",
            description = "Synthetic incident for triage authorization test.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static string? Title(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);
        return document.RootElement.GetProperty("title").GetString();
    }
}
