using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Security;

namespace Tms.SharedKernel.Persistence;

/// <summary>
/// Runs inside every SaveChanges: stamps audit columns, bumps the concurrency version, rejects
/// cross-tenant writes and appends an <see cref="AuditLog"/> row — all in the caller's transaction.
/// </summary>
internal sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> InfrastructureProperties =
        [nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedAt), nameof(IAuditable.ModifiedBy), nameof(AggregateRoot.Version)];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Process(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Process(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Process(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not (AuditLog or OutboxMessage) && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var audits = new List<AuditLog>(entries.Count);
        foreach (var entry in entries)
        {
            EnforceTenant(entry);
            Stamp(entry, now);
            if (BuildAudit(entry, now) is { } audit)
            {
                audits.Add(audit);
            }
        }

        if (audits.Count > 0)
        {
            context.Set<AuditLog>().AddRange(audits);
        }
    }

    private void EnforceTenant(EntityEntry entry)
    {
        if (entry.Entity is not ITenantScoped scoped || currentUser.TenantId is not { } callerTenant)
        {
            return; // system context (login, seeding, migrations): no caller tenant to enforce
        }

        if (scoped.TenantId != callerTenant)
        {
            throw new TenantViolationException(
                $"Attempted to {entry.State.ToString().ToLowerInvariant()} {entry.Metadata.ShortName()} of tenant {scoped.TenantId} from tenant {callerTenant}.");
        }
    }

    private void Stamp(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is not IAuditable)
        {
            return;
        }

        switch (entry.State)
        {
            case EntityState.Added:
                entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
                entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = currentUser.UserId;
                break;
            case EntityState.Modified:
                entry.Property(nameof(IAuditable.ModifiedAt)).CurrentValue = now;
                entry.Property(nameof(IAuditable.ModifiedBy)).CurrentValue = currentUser.UserId;
                if (entry.Entity is AggregateRoot)
                {
                    var version = entry.Property(nameof(AggregateRoot.Version));
                    version.CurrentValue = (long)version.CurrentValue! + 1;
                }

                break;
        }
    }

    private AuditLog? BuildAudit(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Metadata.ClrType.IsDefined(typeof(AuditIgnoreAttribute), inherit: true))
        {
            return null;
        }

        var changes = new SortedDictionary<string, AuditChange>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (InfrastructureProperties.Contains(name) || property.Metadata.PropertyInfo?.IsDefined(typeof(AuditIgnoreAttribute), inherit: true) == true)
            {
                continue;
            }

            var masked = property.Metadata.PropertyInfo?.IsDefined(typeof(AuditMaskAttribute), inherit: true) == true;
            object? Show(object? value) => masked ? Mask(value) : value;

            switch (entry.State)
            {
                case EntityState.Added when property.CurrentValue is not null:
                    changes[name] = new AuditChange(null, Show(property.CurrentValue));
                    break;
                case EntityState.Deleted:
                    changes[name] = new AuditChange(Show(property.OriginalValue), null);
                    break;
                case EntityState.Modified when property.IsModified:
                    changes[name] = new AuditChange(Show(property.OriginalValue), Show(property.CurrentValue));
                    break;
            }
        }

        if (entry.State == EntityState.Modified && changes.Count == 0)
        {
            return null;
        }

        var key = entry.Metadata.FindPrimaryKey()!;
        return new AuditLog
        {
            TenantId = (entry.Entity as ITenantScoped)?.TenantId ?? currentUser.TenantId,
            UserId = currentUser.UserId,
            EntityType = entry.Metadata.ShortName(),
            EntityId = string.Join('|', key.Properties.Select(p => entry.Property(p.Name).CurrentValue)),
            Action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                _ => AuditAction.Updated,
            },
            Changes = JsonSerializer.Serialize(changes, JsonOptions),
            OccurredAt = now,
            TraceId = currentUser.TraceId,
            IpAddress = currentUser.IpAddress,
        };
    }

    private static string? Mask(object? value) => value switch
    {
        null => null,
        string { Length: > 4 } text => new string('•', text.Length - 4) + text[^4..],
        string text => new string('•', text.Length),
        _ => "•••",
    };

    private sealed record AuditChange(object? Old, object? New);
}
