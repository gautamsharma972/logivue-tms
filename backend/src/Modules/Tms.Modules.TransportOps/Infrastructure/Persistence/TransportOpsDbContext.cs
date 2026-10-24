using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.TransportOps.Infrastructure.Persistence;

public sealed class TransportOpsDbContext(DbContextOptions<TransportOpsDbContext> options, ICurrentUser currentUser) : TmsDbContext(options, currentUser)
{
    /// <summary>Tables are prefixed <c>to_</c> (transporter operations): <c>to_lrs</c>, <c>to_trips</c>, <c>to_invoices</c>…</summary>
    public const string Schema = "to";

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<OpsLocation> Locations => Set<OpsLocation>();

    public DbSet<CustomerAgreement> Agreements => Set<CustomerAgreement>();

    public DbSet<AgreementRate> AgreementRates => Set<AgreementRate>();

    public DbSet<DeliveryRequest> Requests => Set<DeliveryRequest>();

    public DbSet<RequestLine> RequestLines => Set<RequestLine>();

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<UnitOfMeasure> Uoms => Set<UnitOfMeasure>();

    public DbSet<Lr> Lrs => Set<Lr>();

    public DbSet<LrLine> LrLines => Set<LrLine>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<TripStop> TripStops => Set<TripStop>();

    public DbSet<DeliveryEvent> Events => Set<DeliveryEvent>();

    public DbSet<Pod> Pods => Set<Pod>();

    public DbSet<PodDocument> PodDocuments => Set<PodDocument>();

    public DbSet<PodReview> PodReviews => Set<PodReview>();

    public DbSet<FreightCalculation> Calculations => Set<FreightCalculation>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<InvoiceSubmission> Submissions => Set<InvoiceSubmission>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<InvoiceSeries> InvoiceSeries => Set<InvoiceSeries>();

    public DbSet<OpsSetting> Settings => Set<OpsSetting>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportOpsDbContext).Assembly);
    }
}
