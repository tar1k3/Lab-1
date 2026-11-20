using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public class CookieDefenseTests(SecureLabApiFactory f, ITestOutputHelper o) : IClassFixture<SecureLabApiFactory>
{
  static readonly string Name = "DefenseUnused";

  [Fact]
  public async Task BobLogin_Http()
  {
    var host = f.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.PostConfigure<CookieAuthenticationOptions>(
    Name, x => x.Cookie.SecurePolicy = CookieSecurePolicy.Always)));
    var client = host.CreateClient(new() { BaseAddress = new Uri("http://localhost") });
    var res = await client.PostAsJsonAsync("/api/auth/login", 
    new { userName = "bob", password = Environment.GetEnvironmentVariable("SeedUsers__Password") });
    var attrs = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Identity.Application="))
    .Split(';').Skip(1).Select(a => a.Trim().ToLower()).ToArray();
    o.WriteLine($"{Name} | login {(int)res.StatusCode} | {string.Join("; ", attrs)}");
    Assert.Equal(Name == IdentityConstants.ApplicationScheme, attrs.Contains("secure"));
    }
  }
