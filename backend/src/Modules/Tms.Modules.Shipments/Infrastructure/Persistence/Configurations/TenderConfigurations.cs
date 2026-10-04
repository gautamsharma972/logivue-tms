using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Shipments.Domain;

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Configurations;

internal sealed class TenderRoundConfiguration : IEntityTypeConfiguration<TenderRound>
{
    public void Configure(EntityTypeBuilder<TenderRound> builder)
    {
        builder.ToTable("tender_rounds");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Number).HasMaxLength(20).IsRequired();
        builder.Property(t => t.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Mode).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Notes).HasMaxLength(500);
        builder.Property(t => t.CloseReason).HasMaxLength(500);
        builder.Ignore(t => t.IsOpen);
        builder.Ignore(t => t.Current);

        builder.HasMany(t => t.Invitees).WithOne().HasForeignKey(i => i.TenderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Invitees).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(t => t.Events).WithOne().HasForeignKey(e => e.TenderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Events).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => new { t.TenantId, t.Number }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.ShipmentId, t.Status });
    }
}

internal sealed class TenderInviteeConfiguration : IEntityTypeConfiguration<TenderInvitee>
{
    public void Configure(EntityTypeBuilder<TenderInvitee> builder)
    {
        builder.ToTable("tender_invitees");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.ContractReference).HasMaxLength(30).IsRequired();
        builder.Property(i => i.QuotedTotal).HasPrecision(18, 2);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(i => i.Reason).HasMaxLength(500);
        builder.Property(i => i.CounterRate).HasPrecision(18, 2);
        builder.Property(i => i.AgreedRate).HasPrecision(18, 2);
        builder.Property(i => i.CounterComment).HasMaxLength(500);
        builder.Property(i => i.CounterStatus).HasConversion<string>().HasMaxLength(10);
        builder.Property(i => i.BidVehicleRegistration).HasMaxLength(12);
        builder.Property(i => i.BidDriverName).HasMaxLength(150);
        builder.Ignore(i => i.IsLive);

        builder.HasIndex(i => new { i.TenantId, i.TenderId, i.TransporterId }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.TransporterId, i.Status }); // the vendor's open invitations
        builder.HasIndex(i => new { i.TenantId, i.Status, i.Deadline }); // the expiry sweep
        builder.HasIndex(i => new { i.TenantId, i.ShipmentId });
    }
}

internal sealed class TenderEventConfiguration : IEntityTypeConfiguration<TenderEvent>
{
    public void Configure(EntityTypeBuilder<TenderEvent> builder)
    {
        builder.ToTable("tender_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Comments).HasMaxLength(500);
        builder.HasIndex(e => new { e.TenantId, e.TenderId, e.At });
    }
}
