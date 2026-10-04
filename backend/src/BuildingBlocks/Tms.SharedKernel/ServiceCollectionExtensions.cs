using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tms.SharedKernel.Messaging;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.SharedKernel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<AuditSaveChangesInterceptor>();
        services.TryAddScoped<DomainEventOutboxInterceptor>();
        services.AddOptions<OutboxOptions>().BindConfiguration(OutboxOptions.SectionName);
        services.AddDomainEventTypes(typeof(ServiceCollectionExtensions).Assembly);
        services.TryAddSingleton<DomainEventRegistry>();
        services.AddOptions<FieldEncryptionOptions>().BindConfiguration(FieldEncryptionOptions.SectionName);
        services.TryAddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();
        services.TryAddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();
        return services;
    }

    /// <summary>Adds the standard TMS interceptors (audit, tenant guard, domain events) to a module DbContext.</summary>
    public static DbContextOptionsBuilder UseTmsInterceptors(this DbContextOptionsBuilder options, IServiceProvider services) =>
        options.AddInterceptors(
            services.GetRequiredService<AuditSaveChangesInterceptor>(),
            services.GetRequiredService<DomainEventOutboxInterceptor>());

    /// <summary>Makes the domain events in <paramref name="assembly"/> deliverable from the outbox.</summary>
    public static IServiceCollection AddDomainEventTypes(this IServiceCollection services, Assembly assembly)
    {
        services.AddSingleton(new DomainEventTypeSource(assembly));
        return services;
    }

    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlers = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in handlers)
        {
            services.AddScoped(service, implementation);
        }

        return services;
    }
}
