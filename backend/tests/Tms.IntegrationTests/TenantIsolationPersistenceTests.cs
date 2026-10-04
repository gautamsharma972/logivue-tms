using Microsoft.EntityFrameworkCore;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.IntegrationTests;

/// <summary>Exercises the tenant guard and query filter directly, below the HTTP layer.</summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationPersistenceTests
{
    private sealed class FakeUser(Guid? tenantId) : ICurrentUser
    {
        public Guid? UserId => Guid.Empty;

        public Guid? TenantId => tenantId;

        public Guid? TransporterId => null;

        public bool IsAuthenticated => tenantId is not null;

        public string? IpAddress => null;

        public string? TraceId => null;

        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
    }

    private static PlatformDbContext ContextFor(Guid? tenantId)
    {
        var user = new FakeUser(tenantId);
        var options = new DbContextOptionsBuilder<PlatformDbContext>();
        PlatformDbContextOptions.Configure(options, TmsApiFactory.ConnectionString, new Version(8, 4, 0));
        options.AddInterceptors(new AuditSaveChangesInterceptor(user, TimeProvider.System));
        return new PlatformDbContext(options.Options, user);
    }

    private static async Task<Tenant> NewTenantAsync()
    {
        var tenant = Tenant.Create($"T{Guid.NewGuid():N}"[..12], "Isolation test");
        await using var system = ContextFor(null);
        system.Tenants.Add(tenant);
        await system.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task Writing_a_row_for_another_tenant_is_blocked()
    {
        var mine = await NewTenantAsync();
        var theirs = await NewTenantAsync();
        await using var db = ContextFor(mine.Id);

        db.Roles.Add(Role.Create(theirs.Id, "Smuggled", null, []));

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Queries_only_return_the_callers_tenant_and_nothing_without_one()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        await using (var asA = ContextFor(a.Id))
        {
            asA.Roles.Add(Role.Create(a.Id, "A-role", null, []));
            await asA.SaveChangesAsync();
        }

        await using (var asB = ContextFor(b.Id))
        {
            asB.Roles.Add(Role.Create(b.Id, "B-role", null, []));
            await asB.SaveChangesAsync();
        }

        await using var readA = ContextFor(a.Id);
        (await readA.Roles.Select(r => r.Name).ToListAsync()).ShouldBe(["A-role"]);

        await using var readNone = ContextFor(null);
        (await readNone.Roles.ToListAsync()).ShouldBeEmpty("no tenant context must fail closed");
    }
}
