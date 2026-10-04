using System.Text;
using System.Text.Json.Serialization;
using LogiVue.Tms.Api.Common;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.TransporterManagement.Api;
using LogiVue.Tms.TransporterManagement.Infrastructure;
using LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Bootstrap logger captures start-up failures before the host is built.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, configuration) => configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter()));

    // ---- Persistence ----
    var connectionString = builder.Configuration.GetConnectionString("TmsDb")
        ?? throw new InvalidOperationException("ConnectionStrings:TmsDb is not configured.");

    var serverVersion = new Version(8, 0, 36);

    // Transporter Management owns its own context and migrations history (see TransporterDbContext).
    var documentStoragePath = builder.Configuration["TransporterManagement:DocumentStoragePath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "tm-documents");
    builder.Services.AddTransporterManagementInfrastructure(connectionString, serverVersion, documentStoragePath);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddSingleton(TimeProvider.System);

    // ---- Authentication & authorisation ----
    // Every endpoint requires a signed-in caller unless it is marked [AllowAnonymous]. Roles and transporter scope
    // come from the token: "roles" and "transporter_id". Configure either an OpenID Connect authority or a signing key.
    var auth = builder.Configuration.GetSection("Authentication");
    var authority = auth["Authority"];
    var signingKey = auth["SigningKey"];
    if (string.IsNullOrWhiteSpace(authority) && string.IsNullOrWhiteSpace(signingKey))
    {
        throw new InvalidOperationException("Configure Authentication:Authority or Authentication:SigningKey.");
    }
    // The signing key is a development convenience. Anywhere else, tokens must come from a real identity provider.
    if (!builder.Environment.IsDevelopment())
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("Configure Authentication:Authority outside Development. Production tokens come from the identity provider.");
        }

        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Authentication:SigningKey is for development only. Remove it when running outside Development.");
        }
    }

    // Without an issuer and audience every token would fail validation silently, so start-up refuses instead.
    if (string.IsNullOrWhiteSpace(auth["Issuer"]) || string.IsNullOrWhiteSpace(auth["Audience"]))
    {
        throw new InvalidOperationException("Configure Authentication:Issuer and Authentication:Audience. Run the API from its project folder so appsettings.json is loaded.");
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.Authority = string.IsNullOrWhiteSpace(authority) ? null : authority;
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = auth["Issuer"],
                ValidateAudience = true,
                ValidAudience = auth["Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = "sub",
                RoleClaimType = "roles",
                IssuerSigningKey = string.IsNullOrWhiteSpace(signingKey)
                    ? null
                    : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
            };
        });

    builder.Services.AddAuthorization(options =>
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();

    // ---- Validation & errors ----
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // ---- Web ----
    builder.Services.AddTransporterManagementApi()
        .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "LogiVue TMS API",
            Version = "v1",
            Description = "Transport management: transporter master, onboarding, compliance, eligibility, tendering, placement, execution and performance."
        }));

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();

    if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "LogiVue TMS v1"));
    }

    app.UseCors("Frontend");
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();

    await InitialiseDatabaseAsync(app);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "LogiVue TMS API terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    if (!app.Configuration.GetValue("Database:AutoMigrate", true))
    {
        return;
    }

    using var scope = app.Services.CreateScope();
    var tmDb = scope.ServiceProvider.GetRequiredService<TransporterDbContext>();
    await tmDb.Database.MigrateAsync();
}

public partial class Program;
