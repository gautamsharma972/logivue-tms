using System.Reflection;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tms.BuildingBlocks.Web.Email;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.BuildingBlocks.Web.Storage;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Files;
using Tms.SharedKernel.Security;

namespace Tms.BuildingBlocks.Web;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebBuildingBlocks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<HttpCurrentUser>();
        services.TryAddScoped<AmbientCurrentUser>();
        services.TryAddScoped<ICurrentUser>(sp => sp.GetRequiredService<AmbientCurrentUser>());
        services.TryAddScoped<IAmbientUserContext>(sp => sp.GetRequiredService<AmbientCurrentUser>());

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
            });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.TryAddSingleton<IEmailSender>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value.Provider.Equals("Smtp", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp)
                : ActivatorUtilities.CreateInstance<LoggingEmailSender>(sp));

        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.TryAddSingleton<IFileStore, LocalFileStore>();
        services.TryAddSingleton<IFileScanner, NoFileScanner>();

        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        var authPermits = configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPermits,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    /// <summary>Registers every class named *Handler under <paramref name="namespacePrefix"/> as a scoped service.</summary>
    public static IServiceCollection AddHandlers(this IServiceCollection services, Assembly assembly, string namespacePrefix)
    {
        var handlers = assembly.GetTypes().Where(t =>
            t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler", StringComparison.Ordinal)
            && t.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true);

        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }

        return services;
    }

    /// <summary>Registers every FluentValidation validator found in the assembly of <typeparamref name="TMarker"/>.</summary>
    public static IServiceCollection AddValidatorsFrom<TMarker>(this IServiceCollection services) =>
        services.AddValidatorsFromAssemblyContaining<TMarker>(includeInternalTypes: true);
}
