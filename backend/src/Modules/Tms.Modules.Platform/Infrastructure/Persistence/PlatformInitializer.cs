using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tms.Modules.Platform.Application;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Security;

namespace Tms.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformInitializationOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF migrations at startup. Intended for development; production deploys migrate explicitly.</summary>
    public bool MigrateOnStartup { get; init; }

    /// <summary>Create a demo tenant with an administrator when the database has no tenants (development only).</summary>
    public bool SeedDemoData { get; init; }

    public string DemoTenantCode { get; init; } = "DEMO";

    public string DemoTenantName { get; init; } = "Demo Logistics Pvt Ltd";

    public string DemoAdminEmail { get; init; } = "admin@demo.tms";

    public string? DemoAdminPassword { get; init; }
}

internal sealed class PlatformInitializer(
    PlatformDbContext db,
    PermissionCatalog catalog,
    IPasswordService passwords,
    ILogger<PlatformInitializer> logger)
{
    public async Task InitialiseAsync(PlatformInitializationOptions options, CancellationToken cancellationToken)
    {
        if (options.MigrateOnStartup)
        {
            logger.LogInformation("Applying platform migrations");
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (options.SeedDemoData && !await db.Tenants.AnyAsync(cancellationToken))
        {
            await SeedDemoTenantAsync(options, cancellationToken);
        }

        await SyncSystemRolesAsync(cancellationToken);
    }

    private async Task SeedDemoTenantAsync(PlatformInitializationOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.DemoAdminPassword))
        {
            throw new InvalidOperationException("Database:DemoAdminPassword must be set when Database:SeedDemoData is enabled.");
        }

        var tenant = Tenant.Create(options.DemoTenantCode, options.DemoTenantName);
        var admin = Role.Create(tenant.Id, Role.AdministratorName, "Full access to everything in this organisation.", catalog.Codes, isSystem: true);
        var user = User.Create(tenant.Id, options.DemoAdminEmail, "Demo Administrator", passwords.Hash(options.DemoAdminPassword), UserType.Internal, [admin]);

        db.AddRange(tenant, admin, user);
        await db.SaveChangesAsync(cancellationToken);
        var (code, email) = (tenant.Code, user.Email);
        logger.LogWarning("Seeded demo tenant {TenantCode} with administrator {Email}. Never enable this in production.", code, email);
    }

    /// <summary>The Administrator role always holds every permission, including those added by newly deployed modules.</summary>
    private async Task SyncSystemRolesAsync(CancellationToken cancellationToken)
    {
        var systemRoles = await db.Roles.IgnoreQueryFilters().Where(r => r.IsSystem).ToListAsync(cancellationToken);
        systemRoles.ForEach(r => r.SyncSystemPermissions(catalog.Codes));

        if (db.ChangeTracker.HasChanges())
        {
            var changed = db.ChangeTracker.Entries<Role>().Count(e => e.State == EntityState.Modified);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Synchronised permissions of {Count} system role(s)", changed);
        }
    }
}
