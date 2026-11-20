using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public class AuditDefenseTests(SecureLabApiFactory f, ITestOutputHelper o) : IClassFixture<SecureLabApiFactory>
{
    static readonly Guid I4 = Guid.Parse("20000000-0000-0000-0000-000000000004");

    static List<string> Check(IReadOnlyDictionary<string, string> e, Guid subject, string trace)
    {
        var errors = new List<string>();
        if (e.GetValueOrDefault("UserId") != subject.ToString()) errors.Add("P1 subject");
        if (e.GetValueOrDefault("TraceId") != trace) errors.Add("P2 trace");
        return errors;
    }

    [Fact]
    public void Synthetic_ForgedSubject()
    {
        var fake = new Dictionary<string, string> { ["UserId"] = DbSeeder.AliceId.ToString(), ["TraceId"] = "00-syn" };
        Assert.Equal(new[] { "P1 subject" }, Check(fake, DbSeeder.BobId, "00-syn"));
    }

    [Fact]
    public async Task Bob_TriageI4_Denied()
    {
        var audit = new AuditCollector();
        using var host = f.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(audit)));
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "bob", password = Environment.GetEnvironmentVariable("SeedUsers__Password") });
        var bobId = (await client.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("id").GetGuid();

        var status = async () =>
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            return await db.Incidents.AsNoTracking().Where(i => i.Id == I4).Select(i => i.Status).SingleAsync();
        };

        var before = await status();
        var res = await client.PostAsync($"/api/incidents/{I4}/triage", null);
        var trace = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("traceId").GetString()!;
        var after = await status();
        var e = Assert.Single(audit.Records, r => r.GetValueOrDefault("IncidentId") == I4.ToString());

        o.WriteLine($"response {(int)res.StatusCode} trace {trace} | state {before} -> {after}");
        o.WriteLine("audit " + string.Join(", ", e.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Empty(Check(e, bobId, trace));
        Assert.Equal(IncidentStatus.New, after);
    }
}

public class AuditCollector : ILoggerProvider, ILogger
{
    public List<Dictionary<string, string>> Records = new();
    public ILogger CreateLogger(string category) => category == "SecureLab.Audit" ? this : NullLogger.Instance;
    public void Dispose() { }
    public IDisposable? BeginScope<T>(T state) where T : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<T>(LogLevel level, EventId id, T state, Exception? ex, Func<T, Exception?, string> fmt)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> fields)
            lock (Records) Records.Add(fields.Where(x => x.Key != "{OriginalFormat}").ToDictionary(x => x.Key, x => $"{x.Value}"));
    }
}
