using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Pod;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Configurations;

internal sealed class LoadExecutionConfiguration : IEntityTypeConfiguration<LoadExecution>
{
    public void Configure(EntityTypeBuilder<LoadExecution> b)
    {
        b.ToTable("tm_load_executions");
        b.Property(x => x.LoadReference).HasMaxLength(50).IsRequired();
        b.Property(x => x.PickupDelayReasonCode).HasMaxLength(40);
        b.Property(x => x.DeliveryDelayReasonCode).HasMaxLength(40);
        b.Property(x => x.PickupAttribution).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.DeliveryAttribution).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.LoadReference, x.TransporterId }).IsUnique();
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => x.PlannedPickupAt);
        b.HasIndex(x => x.PlannedDeliveryAt);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LoadExecutionEventConfiguration : IEntityTypeConfiguration<LoadExecutionEvent>
{
    public void Configure(EntityTypeBuilder<LoadExecutionEvent> b)
    {
        b.ToTable("tm_load_execution_events");
        b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.DelayReasonCode).HasMaxLength(40);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.RecordedBy).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.LoadExecutionId);
        b.HasOne<LoadExecution>().WithMany().HasForeignKey(x => x.LoadExecutionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PodRecordConfiguration : IEntityTypeConfiguration<PodRecord>
{
    public void Configure(EntityTypeBuilder<PodRecord> b)
    {
        b.ToTable("tm_pod_records");
        b.Property(x => x.LoadReference).HasMaxLength(50).IsRequired();
        b.Property(x => x.ReceivedBy).HasMaxLength(150);
        b.Property(x => x.FileReference).HasMaxLength(200);
        b.Property(x => x.OriginalFileName).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RejectionReason).HasMaxLength(1000);
        b.Property(x => x.ReviewedBy).HasMaxLength(100);
        b.HasIndex(x => x.LoadExecutionId).IsUnique();
        b.HasIndex(x => new { x.TransporterId, x.Status });
        b.HasIndex(x => x.DueAt);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LoadExecution>().WithMany().HasForeignKey(x => x.LoadExecutionId).OnDelete(DeleteBehavior.Restrict);
    }
}
