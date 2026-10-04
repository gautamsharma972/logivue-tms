using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Security;

namespace Tms.SharedKernel.Persistence;

/// <summary>
/// Base for every module DbContext. Provides, uniformly: tenant isolation (global query filter),
/// optimistic concurrency on aggregate roots, and the shared audit-log mapping.
/// </summary>
public abstract class TmsDbContext(DbContextOptions options, ICurrentUser currentUser) : DbContext(options)
{
    /// <summary>The module that owns (migrates) the shared audit and outbox tables. Its schema prefixes the table name.</summary>
    public const string AuditSchema = "platform";

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(TmsDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected virtual bool OwnsSharedTables => false;

    /// <summary>Evaluated per query by EF Core. No tenant ⇒ no rows (fail closed).</summary>
    private Guid? CurrentTenantId => currentUser.TenantId;

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureModel(modelBuilder);

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_logs", AuditSchema, t =>
            {
                if (!OwnsSharedTables)
                {
                    t.ExcludeFromMigrations();
                }
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.EntityType).HasMaxLength(128).IsRequired();
            b.Property(x => x.EntityId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Action).HasConversion<string>().HasMaxLength(16);
            b.Property(x => x.Changes).HasColumnType("json");
            b.Property(x => x.TraceId).HasMaxLength(64);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
            b.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages", AuditSchema, t =>
            {
                if (!OwnsSharedTables)
                {
                    t.ExcludeFromMigrations();
                }
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Type).HasMaxLength(300).IsRequired();
            b.Property(x => x.Payload).HasColumnType("json").IsRequired();
            b.Property(x => x.LastError).HasMaxLength(2000);
            b.HasIndex(x => new { x.ProcessedAt, x.DeadLetteredAt, x.NextAttemptAt });
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null))
        {
            if (typeof(AggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(AggregateRoot.Version)).IsConcurrencyToken();
            }

            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
