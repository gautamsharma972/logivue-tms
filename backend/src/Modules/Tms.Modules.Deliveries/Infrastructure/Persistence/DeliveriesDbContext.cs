using Microsoft.EntityFrameworkCore;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Deliveries.Infrastructure.Persistence;

public sealed class DeliveriesDbContext(DbContextOptions<DeliveriesDbContext> options, ICurrentUser currentUser) : TmsDbContext(options, currentUser)
{
    /// <summary>Tables are prefixed <c>pd_</c> (proof of delivery): <c>pd_deliveries</c>, <c>pd_pod_records</c>…</summary>
    public const string Schema = "pd";

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<DeliveryItem> Items => Set<DeliveryItem>();

    public DbSet<DeliveryAttempt> Attempts => Set<DeliveryAttempt>();

    public DbSet<DeliveryEvent> Events => Set<DeliveryEvent>();

    public DbSet<DeliveryDiscrepancy> Discrepancies => Set<DeliveryDiscrepancy>();

    public DbSet<PodRecord> Pods => Set<PodRecord>();

    public DbSet<PodItem> PodItems => Set<PodItem>();

    public DbSet<PodEvidence> Evidence => Set<PodEvidence>();

    public DbSet<PodSignature> Signatures => Set<PodSignature>();

    public DbSet<PodValidationResult> Validations => Set<PodValidationResult>();

    public DbSet<PodReviewAction> ReviewActions => Set<PodReviewAction>();

    public DbSet<PodOcrResult> OcrResults => Set<PodOcrResult>();

    public DbSet<PodOcrField> OcrFields => Set<PodOcrField>();

    public DbSet<DeliveryException> Exceptions => Set<DeliveryException>();

    public DbSet<ExceptionNote> ExceptionNotes => Set<ExceptionNote>();

    public DbSet<SyncRecord> SyncRecords => Set<SyncRecord>();

    public DbSet<DeliverySetting> Settings => Set<DeliverySetting>();

    public DbSet<DeliveryNotification> Notifications => Set<DeliveryNotification>();

    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();

    public DbSet<ClaimHandoff> ClaimHandoffs => Set<ClaimHandoff>();

    public DbSet<IntegrationMessage> IntegrationMessages => Set<IntegrationMessage>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveriesDbContext).Assembly);
    }
}
