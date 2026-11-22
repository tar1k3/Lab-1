using System.Net;
using System.Net.Http.Json;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

// ЛР 6: ліміт пошуку 5 запитів / 10 с на користувача; окремий host — власний лічильник
public sealed class SearchRateLimitTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private sealed record Item(Guid Id);

    private static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task Search_QuotaPerUser_Returns429_OtherUserUnaffected_RecoversAfterWindow()
    {
        using var host = factory.WithWebHostBuilder(_ => { });
        using var alice = await host.LoginAsync("alice", Password);
        using var bob = await host.LoginAsync("bob", Password);

        // Перші 5 пошуків alice -> 200; результати лише в межах її доступу
        for (var i = 0; i < 5; i++)
        {
            using var ok = await alice.GetAsync("/api/incidents/search?q=");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var items = await ok.Content.ReadFromJsonAsync<List<Item>>();
            Assert.Contains(items!, x => x.Id == DbSeeder.AliceIncidentId);
            Assert.DoesNotContain(items!, x => x.Id == DbSeeder.BobIncidentId);
        }

        // 6-й пошук alice -> 429
        using var limited = await alice.GetAsync("/api/incidents/search?q=");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // Bob у той самий момент -> 200: окремий лічильник
        using var bobResponse = await bob.GetAsync("/api/incidents/search?q=");
        Assert.Equal(HttpStatusCode.OK, bobResponse.StatusCode);

        // Під час ліміту пошуку authorization не змінюється: alice бачить своє і не бачить чужого
        using var own = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        using var foreign = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // Після вікна (10 с) пошук знову доступний
        await Task.Delay(TimeSpan.FromSeconds(11));
        using var recovered = await alice.GetAsync("/api/incidents/search?q=");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }
}
