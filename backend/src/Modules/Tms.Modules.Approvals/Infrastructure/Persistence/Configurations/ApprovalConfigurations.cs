using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Approvals.Domain;

namespace Tms.Modules.Approvals.Infrastructure.Persistence.Configurations;

internal sealed class ApprovalPolicyConfiguration : IEntityTypeConfiguration<ApprovalPolicy>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicy> builder)
    {
        builder.ToTable("policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.DocumentType).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Steps).HasJsonList();
        builder.HasIndex(p => new { p.TenantId, p.DocumentType }).IsUnique();
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.DocumentType).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Title).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.CurrentPermission).HasMaxLength(128);
        builder.Property(r => r.DeciderKey).HasMaxLength(1024).IsRequired();
        builder.Property(r => r.Steps).HasJsonList();
        builder.Ignore(r => r.CurrentStep);

        // "My inbox": pending requests whose current step needs a permission I hold.
        builder.HasIndex(r => new { r.TenantId, r.Status, r.CurrentPermission });
        builder.HasIndex(r => new { r.TenantId, r.RequesterId, r.CreatedAt });
        builder.HasIndex(r => new { r.TenantId, r.DocumentType, r.DocumentId });
    }
}

internal sealed class DelegationConfiguration : IEntityTypeConfiguration<Delegation>
{
    public void Configure(EntityTypeBuilder<Delegation> builder)
    {
        builder.ToTable("delegations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Reason).HasMaxLength(300);
        builder.HasIndex(d => new { d.TenantId, d.DelegateId });
        builder.HasIndex(d => new { d.TenantId, d.DelegatorId });
    }
}
