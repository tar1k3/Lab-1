using System.Net;

namespace SecureLab.Api.Tests;

public sealed class SearchMechanicsTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Search_ReturnsJson()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/incidents/search?q=навчальн");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
    [Fact]
    public async Task Search_Literal()
    {
    const string q = @"50%_off\A";
    var client = factory.CreateClient():
    var url = "api/incidents/search?q=" + Uri.EscapeDataString(q);
    Assert.Equal("api/incidents/search?q=50%25_off%5CA", url);
    var response = await client.GetAsync(url);
    Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }
}
