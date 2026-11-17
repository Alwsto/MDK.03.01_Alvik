using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StudentLoad.Api;

var migrationOnly = args.FirstOrDefault() == "--migrate-only";
var targetMigration = migrationOnly ? args.ElementAtOrDefault(1) : null;
var builder = WebApplication.CreateBuilder(migrationOnly ? [] : args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

var databasePath = Path.GetFullPath(
    builder.Configuration["STUDENTLOAD_DATABASE"] ?? Path.Combine("Data", "student-load.sqlite"),
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    ForeignKeys = true,
    DefaultTimeout = 10
}.ToString();

builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5188");
builder.Services.AddDbContext<StudentLoadDb>(options => options.UseSqlite(connectionString));
builder.Services.AddDataProtection();
builder.Services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryKeyRepository());
builder.Services.AddSingleton<AccessSessions>();
builder.Services.AddAuthentication(SessionAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthentication>(SessionAuthentication.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("read-workloads", policy => policy.RequireRole(Roles.All))
    .AddPolicy("manage-workloads", policy => policy.RequireRole(Roles.Admin, Roles.Methodist));
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();
var demoAccounts = builder.Configuration.GetValue("STUDENTLOAD_DEMO_ACCOUNTS", true);
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<StudentLoadDb>();
    var pending = (await database.Database.GetPendingMigrationsAsync()).ToArray();
    await database.GetService<IMigrator>().MigrateAsync(targetMigration);
    var applied = await database.Database.GetAppliedMigrationsAsync();
    Console.WriteLine($"SQLite: {databasePath}");
    Console.WriteLine($"Applied migrations: {string.Join(", ", applied)}");
    if (migrationOnly)
    {
        Console.WriteLine($"Pending before invocation: {pending.Length}");
        return;
    }

    await database.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await DemoAccounts.InitializeAsync(database, demoAccounts, builder.Configuration["STUDENTLOAD_INITIAL_ADMIN_PASSWORD"]);
}

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    var loopback = remote is null || IPAddress.IsLoopback(remote) ||
        (remote.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remote.MapToIPv4()));
    if (!loopback && (demoAccounts || !context.Request.IsHttps))
    {
        await Results.Problem("Удалённый доступ требует HTTPS и отключённых демонстрационных учётных записей.",
            statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
        return;
    }

    await next(context);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/auth/login", AuthEndpoints.LoginAsync).RequireRateLimiting("login");
WorkloadEndpoints.Map(app);
await app.RunAsync();
