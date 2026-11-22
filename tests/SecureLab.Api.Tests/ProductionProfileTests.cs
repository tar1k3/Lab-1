using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// Етап 1, п. 4: Production test host. БД готують reset-ом у Development до запуску тесту.
public sealed class ProductionProfileTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private const string StudyConnectionString =
        "Host=127.0.0.1;Port=54329;Database=securelab;Username=securelab;"
        + "Password=local-study-password;Include Error Detail=false";

    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Production_ExpectedErrors_Keep4xxSemantics()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:SecureLab", StudyConnectionString);
        });

        // Передумова: host справді у Production
        Assert.Equal("Production",
            production.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

        // Анонімний запит до захищеного маршруту -> 401
        using var anonymous = production.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymous.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        // Вхід через той самий Production host; дозволений GET -> 204
        using var alice = await production.LoginAsync("alice", Password);
        using var allowed = await alice.GetAsync("/api/auth/session-check");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

        // Некоректний фільтр -> 400 Problem Details
        using var badFilter = await alice.GetAsync("/api/incidents?status=Unknown");
        Assert.Equal(HttpStatusCode.BadRequest, badFilter.StatusCode);
        Assert.Equal("application/problem+json", badFilter.Content.Headers.ContentType?.MediaType);

        // Неіснуючий і чужий інцидент -> 404
        using var missing = await alice.GetAsync("/api/incidents/99999999-9999-9999-9999-999999999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var foreign = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // Triage від Reporter -> 403
        using var triage = await alice.PostAsync($"/api/incidents/{DbSeeder.AliceIncidentId}/triage", null);
        Assert.Equal(HttpStatusCode.Forbidden, triage.StatusCode);
    }
}
