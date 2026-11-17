using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace StudentLoad.Api;

internal sealed class InMemoryKeyRepository : IXmlRepository
{
    private readonly ConcurrentQueue<XElement> _keys = new();

    public IReadOnlyCollection<XElement> GetAllElements() => _keys.Select(key => new XElement(key)).ToArray();

    public void StoreElement(XElement element, string friendlyName) => _keys.Enqueue(new XElement(element));
}

internal static class Roles
{
    public const string Admin = "Admin";
    public const string Methodist = "Methodist";
    public const string Dispatcher = "Dispatcher";
    public const string Teacher = "Teacher";
    public static readonly string[] All = [Admin, Methodist, Dispatcher, Teacher];
}

internal static class DemoAccounts
{
    private const string DemoPassword = "StudentLoad2026!";

    public static async Task InitializeAsync(StudentLoadDb database, bool enabled, string? initialPassword)
    {
        if (enabled)
        {
            var existing = await database.Accounts.Select(row => row.Username).ToListAsync();
            var accounts = new (string Username, string DisplayName, string Role, int? TeacherId)[]
            {
                ("admin", "Заведующий отделением", Roles.Admin, null),
                ("methodist", "Методист учебной части", Roles.Methodist, null),
                ("dispatcher", "Диспетчер учебной части", Roles.Dispatcher, null),
                ("teacher", "Орлова Мария Игоревна", Roles.Teacher, 1)
            };
            foreach (var (username, displayName, role, teacherId) in accounts)
            {
                if (existing.Contains(username))
                    continue;

                database.Accounts.Add(new Account
                {
                    Username = username, DisplayName = displayName, Role = role,
                    TeacherId = teacherId, IsDemo = true,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(DemoPassword, workFactor: 11)
                });
            }

            await database.SaveChangesAsync();
            return;
        }

        if (await database.Accounts.AnyAsync(row => row.Role == Roles.Admin && !row.IsDemo))
            return;

        if (initialPassword is null || initialPassword.Length < 14 || Encoding.UTF8.GetByteCount(initialPassword) > 72)
            throw new InvalidOperationException("Для первичной настройки задайте STUDENTLOAD_INITIAL_ADMIN_PASSWORD: от 14 символов, до 72 байт UTF-8.");

        var admin = await database.Accounts.SingleOrDefaultAsync(row => row.Username == "admin");
        var hash = BCrypt.Net.BCrypt.HashPassword(initialPassword, workFactor: 11);
        if (admin is null)
        {
            database.Accounts.Add(new Account
            {
                Username = "admin", DisplayName = "Заведующий отделением", Role = Roles.Admin,
                IsDemo = false, PasswordHash = hash
            });
        }
        else
        {
            admin.PasswordHash = hash;
            admin.IsDemo = false;
        }

        await database.SaveChangesAsync();
    }
}

internal sealed record LoginRequest(string? Username, string? Password);
internal sealed record LoginReply(string AccessToken, string Username, string DisplayName, string Role, DateTimeOffset ExpiresAtUtc);
internal sealed record AccessSession(int AccountId, string Username, string Role, int? TeacherId, DateTimeOffset ExpiresAtUtc);

internal static class AuthEndpoints
{
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), workFactor: 11);

    public static async Task<IResult> LoginAsync(LoginRequest input, StudentLoadDb database, AccessSessions sessions,
        IConfiguration configuration, HttpContext context, CancellationToken cancellationToken)
    {
        var username = input.Username?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(username) || username.Length > 64 || string.IsNullOrEmpty(input.Password) ||
            Encoding.UTF8.GetByteCount(input.Password) > 72)
        {
            return Results.Problem("Укажите имя пользователя и пароль.", statusCode: StatusCodes.Status400BadRequest);
        }

        var includeDemo = configuration.GetValue("STUDENTLOAD_DEMO_ACCOUNTS", true);
        var account = await database.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Username == username && (includeDemo || !row.IsDemo), cancellationToken);
        var isValid = BCrypt.Net.BCrypt.Verify(input.Password, account?.PasswordHash ?? DummyHash);
        if (!isValid || account is null)
            return Results.Problem("Неверное имя пользователя или пароль.", statusCode: StatusCodes.Status401Unauthorized);

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(sessions.Create(account));
    }
}

internal sealed class AccessSessions
{
    private readonly ConcurrentDictionary<string, AccessSession> _sessions = new(StringComparer.Ordinal);

    public LoginReply Create(Account account)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _sessions)
        {
            if (session.Value.ExpiresAtUtc <= now)
                _sessions.TryRemove(session.Key, out _);
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.AddHours(8);
        _sessions[Digest(token)] = new AccessSession(account.Id, account.Username, account.Role, account.TeacherId, expiresAt);
        return new LoginReply(token, account.Username, account.DisplayName, account.Role, expiresAt);
    }

    public AccessSession? Find(string token)
    {
        if (token.Length != 43 || !_sessions.TryGetValue(Digest(token), out var session))
            return null;

        return session.ExpiresAtUtc > DateTimeOffset.UtcNow ? session : null;
    }

    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

internal sealed class SessionAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AccessSessions sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "StudentLoadBearer";
    public const string TeacherIdClaim = "teacher_id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        const string prefix = "Bearer ";
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var session = sessions.Find(header[prefix.Length..]);
        if (session is null)
            return Task.FromResult(AuthenticateResult.Fail("Недействительный или истёкший токен."));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, session.Username),
            new(ClaimTypes.Role, session.Role)
        };
        if (session.TeacherId is int teacherId)
            claims.Add(new Claim(TeacherIdClaim, teacherId.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
