using System.Net;
using System.Net.Http.Json;

namespace SecureLab.Api.Tests;

public sealed class RegisterTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_DuplicateUserName_Returns409()
    {
        var userName = $"reg{Guid.NewGuid():N}";
        var body1 = new { userName, email = $"{userName}@example.com", displayName = "Test", password = "TestPass123!" };
        using var first = await _client.PostAsJsonAsync("/api/auth/register", body1);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var body2 = new { userName, email = $"other-{Guid.NewGuid():N}@example.com", displayName = "Test2", password = "TestPass123!" };
        using var second = await _client.PostAsJsonAsync("/api/auth/register", body2);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = $"reg{Guid.NewGuid():N}@example.com";
        var body1 = new { userName = $"user1-{Guid.NewGuid():N}", email, displayName = "Test", password = "TestPass123!" };
        using var first = await _client.PostAsJsonAsync("/api/auth/register", body1);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var body2 = new { userName = $"user2-{Guid.NewGuid():N}", email, displayName = "Test2", password = "TestPass123!" };
        using var second = await _client.PostAsJsonAsync("/api/auth/register", body2);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
