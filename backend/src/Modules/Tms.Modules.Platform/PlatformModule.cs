using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tms.BuildingBlocks.Web;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Platform.Application;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Endpoints;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Platform;

/// <summary>Public surface of the Platform module: identity, tenancy, roles, audit trail.</summary>
public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedKernel();
        services.AddDomainEventTypes(typeof(PlatformModule).Assembly);
        if (configuration.GetValue("Outbox:WorkerEnabled", true))
        {
            services.AddHostedService<Infrastructure.Messaging.OutboxProcessor>();
        }


        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var serverVersion = Version.Parse(configuration["Database:ServerVersion"] ?? "8.4.0");

        services.AddDbContext<PlatformDbContext>((sp, options) =>
        {
            PlatformDbContextOptions.Configure(options, connectionString, serverVersion);
            options.UseTmsInterceptors(sp);
        });

        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, TokenService>();
        foreach (var permission in Permissions.All)
        {
            services.AddSingleton(permission);
        }

        services.TryAddSingleton<PermissionCatalog>();
        services.AddScoped<PlatformInitializer>();
        services.AddScoped<Application.Auth.SessionService>();
        services.AddScoped<Application.Auth.PasswordResetService>();
        services.AddScoped<Application.TenantProvisioning>();
        services.AddHandlers(typeof(PlatformModule).Assembly, "Tms.Modules.Platform.Application");
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<ISequenceGenerator, Infrastructure.Persistence.SequenceGenerator>();
        services.AddValidatorsFrom<PlatformDbContext>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false; // keep "sub", "tid", "perm" as issued
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
            });
        services.AddAuthorization();

        return services;
    }

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        AuthEndpoints.Map(app);
        UserEndpoints.Map(app);
        RoleEndpoints.Map(app);
        AuditEndpoints.Map(app);
        return app;
    }

    /// <summary>Runs migrations / demo seeding (per configuration) and keeps system roles in sync with the permission catalog.</summary>
    /// <summary>Operator command behind <c>tenant:create</c>: creates an organisation and an invitation for its first administrator.</summary>
    public static async Task<Tms.SharedKernel.Results.Result<ProvisionedTenant>> ProvisionTenantAsync(
        this IServiceProvider services, ProvisionTenantRequest request, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Application.TenantProvisioning>().ProvisionAsync(request, cancellationToken);
    }

    public static async Task InitialisePlatformAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var options = configuration.GetSection(PlatformInitializationOptions.SectionName).Get<PlatformInitializationOptions>()
            ?? new PlatformInitializationOptions();

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PlatformInitializer>().InitialiseAsync(options, cancellationToken);
    }
}
