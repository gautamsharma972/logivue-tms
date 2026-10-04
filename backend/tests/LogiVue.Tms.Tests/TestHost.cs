using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LogiVue.Tms.Api;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogiVue.Tms.Tests;

/// <summary>
/// Runs the real pipeline (authentication, authorisation, controllers, validation, global exception handler, module wiring).
/// The Transporter Management context uses SQLite, which enforces unique indexes and transactions the way MySQL does.
/// Test identities come from headers through <see cref="TestAuthHandler"/>, so tests act as any role or transporter.
/// </summary>
public class TestHost : WebApplicationFactory<Program>
{
    /// <summary>Enum-aware JSON options matching the host's API configuration.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SqliteConnection _transporterConnection = new("DataSource=:memory:");
    private readonly string _documentRoot = Path.Combine(Path.GetTempPath(), $"lv-tm-docs-{Guid.NewGuid():N}");

    private readonly bool _useTestAuthentication;

    public TestHost() : this(useTestAuthentication: true)
    {
    }

    /// <summary>When false, the host keeps its real JWT authentication and the caller's identity comes from the token.</summary>
    protected TestHost(bool useTestAuthentication)
    {
        _useTestAuthentication = useTestAuthentication;
        _transporterConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:AutoMigrate", "false");
        builder.UseSetting("TransporterManagement:ComplianceEvaluation:Enabled", "false");
        builder.UseSetting("TransporterManagement:DocumentStoragePath", _documentRoot);
        builder.UseSetting("TransporterManagement:TenderExpiry:Enabled", "false");
        builder.UseSetting("TransporterManagement:OverdueMonitor:Enabled", "false");
        builder.UseSetting("Authentication:SigningKey", "test-signing-key-for-jwt-checks-0123456789abcdef");
        builder.UseSetting("Authentication:Issuer", "logivue-tms");
        builder.UseSetting("Authentication:Audience", "logivue-tms-api");

        builder.ConfigureTestServices(services =>
        {
            if (_useTestAuthentication)
            {
                // Identity from headers: the same headers drive authentication (claims) and the ICurrentUser view.
                services.AddScoped<ICurrentUser, HeaderCurrentUser>();
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            }

            services.RemoveAll<DbContextOptions<TransporterDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<TransporterDbContext>>();
            var sqliteProvider = new ServiceCollection().AddEntityFrameworkSqlite().BuildServiceProvider();
            services.AddDbContext<TransporterDbContext>(o => o
                .UseSqlite(_transporterConnection)
                .UseInternalServiceProvider(sqliteProvider));
        });
    }

    /// <summary>Creates the Transporter Management schema (including seed data) before tests run.</summary>
    public async Task EnsureTransporterSchemaAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TransporterDbContext>().Database.EnsureCreatedAsync();
    }

    /// <summary>Runs a database action in a fresh scope, for arranging state or asserting persisted rows.</summary>
    public async Task<T> WithTransporterDbAsync<T>(Func<TransporterDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TransporterDbContext>());
    }

    /// <summary>Runs application services in a fresh scope, for driving time-based transitions in tests.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _transporterConnection.Dispose();
            if (Directory.Exists(_documentRoot))
            {
                Directory.Delete(_documentRoot, recursive: true);
            }
        }
    }
}

/// <summary>Test-only identity read from the X-Test-User, X-Test-Roles and X-Test-Transporter headers.</summary>
internal sealed class HeaderCurrentUser(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : ICurrentUser
{
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";
    public const string TransporterHeader = "X-Test-Transporter";

    private Microsoft.AspNetCore.Http.HttpContext? Context => accessor.HttpContext;

    public string UserId => Context?.Request.Headers[UserHeader].FirstOrDefault() ?? "system";

    public string DisplayName => UserId;

    public long? TransporterId =>
        long.TryParse(Context?.Request.Headers[TransporterHeader].FirstOrDefault(), out var id) ? id : null;

    public IReadOnlyCollection<string> Roles => TestAuthHandler.RolesFrom(Context);

    public bool IsInRole(string role) => Roles.Contains(role);
}

/// <summary>Authenticates requests that carry <c>X-Test-User</c>; without it the request is anonymous and gets 401.</summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[HeaderCurrentUser.UserHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", user) };
        claims.AddRange(RolesFrom(Context).Select(role => new Claim("roles", role)));
        if (!string.IsNullOrWhiteSpace(Request.Headers[HeaderCurrentUser.TransporterHeader].FirstOrDefault()))
        {
            claims.Add(new Claim("transporter_id", Request.Headers[HeaderCurrentUser.TransporterHeader].First()!));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "sub", "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    internal static IReadOnlyCollection<string> RolesFrom(Microsoft.AspNetCore.Http.HttpContext? context) =>
        (context?.Request.Headers[HeaderCurrentUser.RolesHeader].FirstOrDefault() ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Host with the real JWT pipeline, for checks that tokens are validated and claims mapped as configured.</summary>
public sealed class JwtTestHost() : TestHost(useTestAuthentication: false);
