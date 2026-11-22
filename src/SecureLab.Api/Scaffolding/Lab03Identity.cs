using Microsoft.AspNetCore.Identity;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Security;
using System.Security.Claims;
namespace SecureLab.Api.Scaffolding;

public static class Lab03Identity
{
    public static void AddLab03Identity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
        }).AddEntityFrameworkStores<SecureLabDbContext>().AddDefaultTokenProviders();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    public static void MapLab03Identity(this WebApplication app)
    {
       app.MapPost("/api/auth/register", async (RegistrationInput input, UserManager<ApplicationUser> users) =>
{
    if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrWhiteSpace(input.Email)
        || string.IsNullOrWhiteSpace(input.DisplayName) || string.IsNullOrEmpty(input.Password))
        return Results.BadRequest(new { message = "Registration failed" });

    if (input.UserName.Trim().Length < 3 || input.UserName.Trim().Length > 64)
        return Results.BadRequest(new { message = "Registration failed" });

    if (!input.Email.Contains('@') || input.Email.Length > 256)
        return Results.BadRequest(new { message = "Registration failed" });

    if (input.DisplayName.Trim().Length > 120)
        return Results.BadRequest(new { message = "Registration failed" });

    if (input.Password.Length > 128)
        return Results.BadRequest(new { message = "Registration failed" });

    var user = new ApplicationUser
    {
        Id = Guid.NewGuid(),
        UserName = input.UserName.Trim(),
        Email = input.Email.Trim(),
        DisplayName = input.DisplayName.Trim()
    };

    var result = await users.CreateAsync(user, input.Password);
    if (!result.Succeeded)
    {
        if (result.Errors.Any(e => e.Code == "DuplicateUserName" || e.Code == "DuplicateEmail"))
            return Results.Conflict(new { message = "Account already exists" });

        return Results.BadRequest(new { message = "Registration failed" });
    }

    var role = await users.AddToRoleAsync(user, DbSeeder.ReporterRole);
    if (!role.Succeeded) throw new InvalidOperationException("Default role assignment failed.");

    return Results.Created("/api/me", new RegistrationResponse(user.Id, user.UserName, user.DisplayName));
});
        app.MapPost("/api/auth/login", async (
    LoginInput input, SignInManager<ApplicationUser> signIn,
    HttpContext httpContext, ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(input.UserName) || string.IsNullOrEmpty(input.Password))
    {
        logger.LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}",
            "login", "failure", httpContext.TraceIdentifier);
        return Results.Unauthorized();
    }

    var result = await signIn.PasswordSignInAsync(input.UserName, input.Password, false, false);

    // Подія: лише тип, результат і traceId — без логіна, пароля, cookie
    logger.LogInformation("Auth event {EventType}: result {Result}, trace {TraceId}",
        "login", result.Succeeded ? "success" : "failure", httpContext.TraceIdentifier);

    return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
}).RequireRateLimiting("login");

app.MapPost("/api/auth/logout", async (
    SignInManager<ApplicationUser> signIn, HttpContext httpContext, ILogger<Program> logger) =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    await signIn.SignOutAsync();
    logger.LogInformation("Auth event {EventType}: result {Result}, UserId {UserId}, trace {TraceId}",
        "logout", "success", userId, httpContext.TraceIdentifier);
    return Results.NoContent();
}).AddEndpointFilter<AntiforgeryFilter>();

app.MapGet("/api/me", (HttpContext context) =>
{
    var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var userName = context.User.Identity?.Name;
    var role = context.User.FindFirstValue(ClaimTypes.Role);

    return Results.Ok(new MeResponse(Guid.Parse(id!), userName!, role ?? ""));
}).RequireAuthorization();
        // Приклад лише mechanics: не повертає identity і не реалізує оцінювану operation.
        app.MapGet("/api/auth/session-check", () => Results.NoContent()).RequireAuthorization();
    }
}

public sealed record RegistrationInput(string? UserName, string? Email, string? DisplayName, string? Password);
public sealed record LoginInput(string? UserName, string? Password);
public sealed record RegistrationResponse(Guid Id, string UserName, string DisplayName);
public sealed record MeResponse(Guid Id, string UserName, string Role);
