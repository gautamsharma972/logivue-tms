using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.TransportOps.Domain;

namespace Tms.Modules.TransportOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Code).HasMaxLength(30).IsRequired();
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.Property(c => c.Gstin).HasMaxLength(15);
        b.Property(c => c.BillingLine1).HasMaxLength(200);
        b.Property(c => c.BillingCity).HasMaxLength(100);
        b.Property(c => c.BillingState).HasMaxLength(100);
        b.Property(c => c.BillingPincode).HasMaxLength(10);
        b.Property(c => c.BillingContactName).HasMaxLength(150);
        b.Property(c => c.BillingContactEmail).HasMaxLength(200);
        b.Property(c => c.BillingContactPhone).HasMaxLength(20);
        b.Property(c => c.DeliveryContactName).HasMaxLength(150);
        b.Property(c => c.DeliveryContactPhone).HasMaxLength(20);
        b.Property(c => c.CommercialContactName).HasMaxLength(150);
        b.Property(c => c.CommercialContactEmail).HasMaxLength(200);
        b.Property(c => c.BillingRequirements).HasMaxLength(1000);
        b.Property(c => c.PortalMethod).HasMaxLength(60);
        b.Property(c => c.PortalReference).HasMaxLength(200);
        b.Property(c => c.RequiredRequestFields).HasMaxLength(200);
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(12);
        b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
    }
}

internal sealed class OpsLocationConfiguration : IEntityTypeConfiguration<OpsLocation>
{
    public void Configure(EntityTypeBuilder<OpsLocation> b)
    {
        b.ToTable("locations");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.Code).HasMaxLength(30).IsRequired();
        b.Property(l => l.Name).HasMaxLength(200).IsRequired();
        b.Property(l => l.Kind).HasConversion<string>().HasMaxLength(12);
        b.Property(l => l.Line1).HasMaxLength(200).IsRequired();
        b.Property(l => l.City).HasMaxLength(100).IsRequired();
        b.Property(l => l.State).HasMaxLength(100).IsRequired();
        b.Property(l => l.Pincode).HasMaxLength(10);
        b.Property(l => l.Latitude).HasPrecision(9, 6);
        b.Property(l => l.Longitude).HasPrecision(9, 6);
        b.Property(l => l.AccessNotes).HasMaxLength(500);
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(12);
        b.HasIndex(l => new { l.TenantId, l.Code }).IsUnique();
        b.HasIndex(l => new { l.TenantId, l.CustomerId });
    }
}

internal sealed class CustomerAgreementConfiguration : IEntityTypeConfiguration<CustomerAgreement>
{
    public void Configure(EntityTypeBuilder<CustomerAgreement> b)
    {
        b.ToTable("agreements");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Number).HasMaxLength(30).IsRequired();
        b.Property(a => a.Title).HasMaxLength(200).IsRequired();
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(a => a.WeightBasis).HasConversion<string>().HasMaxLength(30);
        b.Property(a => a.VolumetricKgPerCbm).HasPrecision(10, 2);
        b.Property(a => a.MinimumFreight).HasPrecision(14, 2);
        b.Property(a => a.Detention).HasJson();
        b.Property(a => a.Tax).HasJson();
        b.Property(a => a.Billing).HasJson();
        b.Property(a => a.ExtraCharges).HasJson();
        b.HasMany(a => a.Rates).WithOne().HasForeignKey(r => r.AgreementId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(a => a.Rates).AutoInclude(false);
        b.HasIndex(a => new { a.TenantId, a.Number, a.Revision }).IsUnique();
        b.HasIndex(a => new { a.TenantId, a.CustomerId, a.EffectiveFrom });
    }
}

internal sealed class AgreementRateConfiguration : IEntityTypeConfiguration<AgreementRate>
{
    public void Configure(EntityTypeBuilder<AgreementRate> b)
    {
        b.ToTable("agreement_rates");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.OriginCity).HasMaxLength(100);
        b.Property(r => r.OriginState).HasMaxLength(100);
        b.Property(r => r.DestinationCity).HasMaxLength(100);
        b.Property(r => r.DestinationState).HasMaxLength(100);
        b.Property(r => r.Material).HasMaxLength(100);
        b.Property(r => r.Method).HasConversion<string>().HasMaxLength(24);
        b.Property(r => r.Rate).HasPrecision(14, 4);
        b.Property(r => r.MinimumCharge).HasPrecision(14, 2);
        b.Property(r => r.Slabs).HasJson();
        b.HasIndex(r => new { r.TenantId, r.AgreementId });
    }
}

internal sealed class DeliveryRequestConfiguration : IEntityTypeConfiguration<DeliveryRequest>
{
    public void Configure(EntityTypeBuilder<DeliveryRequest> b)
    {
        b.ToTable("requests");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Number).HasMaxLength(30).IsRequired();
        b.Property(r => r.CustomerReference).HasMaxLength(100);
        b.Property(r => r.EmailReference).HasMaxLength(300);
        b.Property(r => r.Priority).HasMaxLength(20).IsRequired();
        b.Property(r => r.VehicleTypeRequested).HasMaxLength(60);
        b.Property(r => r.Instructions).HasMaxLength(1000);
        b.Property(r => r.Remarks).HasMaxLength(1000);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Issues).HasJson();
        b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.RequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(r => new { r.TenantId, r.Number }).IsUnique();
        b.HasIndex(r => new { r.TenantId, r.Status, r.ReceivedAt });
        b.HasIndex(r => new { r.TenantId, r.CustomerId });
    }
}

internal sealed class RequestLineConfiguration : IEntityTypeConfiguration<RequestLine>
{
    public void Configure(EntityTypeBuilder<RequestLine> b)
    {
        b.ToTable("request_lines");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.MaterialCode).HasMaxLength(60);
        b.Property(l => l.MaterialName).HasMaxLength(200).IsRequired();
        b.Property(l => l.Quantity).HasPrecision(14, 3);
        b.Property(l => l.Unit).HasMaxLength(12).IsRequired();
        b.Property(l => l.WeightKg).HasPrecision(14, 3);
        b.Property(l => l.VolumeCbm).HasPrecision(14, 3);
        b.Property(l => l.Dimensions).HasMaxLength(100);
        b.Property(l => l.Remarks).HasMaxLength(300);
    }
}

internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> b)
    {
        b.ToTable("uoms");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();
        b.Property(u => u.Code).HasMaxLength(12).IsRequired();
        b.Property(u => u.Name).HasMaxLength(60).IsRequired();
        b.HasIndex(u => new { u.TenantId, u.Code }).IsUnique();
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("files");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).ValueGeneratedNever();
        b.Property(f => f.FileKey).HasMaxLength(300).IsRequired();
        b.Property(f => f.FileName).HasMaxLength(255).IsRequired();
        b.Property(f => f.ContentType).HasMaxLength(100).IsRequired();
        b.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
        b.HasIndex(f => new { f.TenantId, f.OwnerId });
    }
}

internal sealed class LrConfiguration : IEntityTypeConfiguration<Lr>
{
    public void Configure(EntityTypeBuilder<Lr> b)
    {
        b.ToTable("lrs");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.Number).HasMaxLength(30).IsRequired();
        b.Property(l => l.CustomerReference).HasMaxLength(100);
        b.Property(l => l.IndentNo).HasMaxLength(60);
        b.Property(l => l.VehicleRegistration).HasMaxLength(20);
        b.Property(l => l.Instructions).HasMaxLength(1000);
        b.Property(l => l.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(l => l.TotalWeightKg).HasPrecision(14, 3);
        b.Property(l => l.TotalVolumeCbm).HasPrecision(14, 3);
        // Looked up by the hash of the secret alone before anyone is signed in, so the hash must be unique across tenants.
        b.Property(l => l.TokenHash).HasMaxLength(64);
        b.Property(l => l.TokenCipher).HasMaxLength(400);
        b.HasMany(l => l.Lines).WithOne().HasForeignKey(x => x.LrId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(l => l.TokenHash).IsUnique();
        b.HasIndex(l => new { l.TenantId, l.Number }).IsUnique();
        b.HasIndex(l => new { l.TenantId, l.CustomerId, l.Status });
        b.HasIndex(l => new { l.TenantId, l.TripId });
        b.HasIndex(l => new { l.TenantId, l.VehicleRegistration });
        b.HasIndex(l => new { l.TenantId, l.UnloadedAt });
    }
}

internal sealed class LrLineConfiguration : IEntityTypeConfiguration<LrLine>
{
    public void Configure(EntityTypeBuilder<LrLine> b)
    {
        b.ToTable("lr_lines");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.MaterialCode).HasMaxLength(60);
        b.Property(l => l.MaterialName).HasMaxLength(200);
        b.Property(l => l.Quantity).HasPrecision(14, 3);
        b.Property(l => l.Unit).HasMaxLength(12).IsRequired();
        b.Property(l => l.WeightKg).HasPrecision(14, 3);
        b.Property(l => l.VolumeCbm).HasPrecision(14, 3);
        b.Property(l => l.VolumeCuFt).HasPrecision(14, 3);
        b.Property(l => l.NetWeightKg).HasPrecision(14, 3);
        b.Property(l => l.Dimensions).HasMaxLength(100);
    }
}

internal sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> b)
    {
        b.ToTable("trips");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Number).HasMaxLength(30).IsRequired();
        b.Property(t => t.VehicleRegistration).HasMaxLength(20).IsRequired();
        b.Property(t => t.DriverName).HasMaxLength(150);
        b.Property(t => t.DriverPhone).HasMaxLength(20);
        b.Property(t => t.Remarks).HasMaxLength(500);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        b.HasMany(t => t.Stops).WithOne().HasForeignKey(s => s.TripId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(t => new { t.TenantId, t.Number }).IsUnique();
        b.HasIndex(t => new { t.TenantId, t.Status, t.PlannedDate });
        b.HasIndex(t => new { t.TenantId, t.VehicleRegistration });
    }
}

internal sealed class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> b)
    {
        b.ToTable("trip_stops");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.HasIndex(s => new { s.TenantId, s.TripId, s.Sequence }).IsUnique();
    }
}

internal sealed class DeliveryEventConfiguration : IEntityTypeConfiguration<DeliveryEvent>
{
    public void Configure(EntityTypeBuilder<DeliveryEvent> b)
    {
        b.ToTable("events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.Remarks).HasMaxLength(500);
        b.Property(e => e.ClientKey).HasMaxLength(100);
        b.Property(e => e.OnceKey).HasMaxLength(80);
        b.Property(e => e.Verification).HasMaxLength(40);
        b.Property(e => e.TokenRef).HasMaxLength(16);
        b.Property(e => e.Latitude).HasPrecision(9, 6);
        b.Property(e => e.Longitude).HasPrecision(9, 6);
        b.Property(e => e.DestinationLatitude).HasPrecision(9, 6);
        b.Property(e => e.DestinationLongitude).HasPrecision(9, 6);
        // A second arrival or unloading for the same LR is refused by the database itself (NULL for every other kind of event).
        b.HasIndex(e => new { e.TenantId, e.OnceKey }).IsUnique();
        b.HasIndex(e => new { e.TenantId, e.ClientKey }).IsUnique();
        b.HasIndex(e => new { e.TenantId, e.LrId, e.ActualAt });
        b.HasIndex(e => new { e.TenantId, e.TripId, e.ActualAt });
    }
}

internal sealed class PodConfiguration : IEntityTypeConfiguration<Pod>
{
    public void Configure(EntityTypeBuilder<Pod> b)
    {
        b.ToTable("pods");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.RejectionReason).HasMaxLength(500);
        b.HasMany(p => p.Documents).WithOne().HasForeignKey(d => d.PodId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Reviews).WithOne().HasForeignKey(r => r.PodId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(p => new { p.TenantId, p.LrId }).IsUnique();
        b.HasIndex(p => new { p.TenantId, p.Status });
    }
}

internal sealed class PodDocumentConfiguration : IEntityTypeConfiguration<PodDocument>
{
    public void Configure(EntityTypeBuilder<PodDocument> b)
    {
        b.ToTable("pod_documents");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.FileKey).HasMaxLength(300).IsRequired();
        b.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        b.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
        b.Property(d => d.Source).HasConversion<string>().HasMaxLength(12);
        b.Property(d => d.Remarks).HasMaxLength(500);
        b.Property(d => d.TokenRef).HasMaxLength(16);
        b.Property(d => d.ClientKey).HasMaxLength(100);
        b.HasIndex(d => new { d.TenantId, d.PodId });
        b.HasIndex(d => new { d.TenantId, d.ClientKey }).IsUnique();
    }
}

internal sealed class PodReviewConfiguration : IEntityTypeConfiguration<PodReview>
{
    public void Configure(EntityTypeBuilder<PodReview> b)
    {
        b.ToTable("pod_reviews");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Action).HasMaxLength(20).IsRequired();
        b.Property(r => r.Reason).HasMaxLength(500);
    }
}

internal sealed class FreightCalculationConfiguration : IEntityTypeConfiguration<FreightCalculation>
{
    public void Configure(EntityTypeBuilder<FreightCalculation> b)
    {
        b.ToTable("calculations");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.LrNumber).HasMaxLength(30).IsRequired();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(14);
        b.Property(c => c.WeightBasisUsed).HasMaxLength(16);
        b.Property(c => c.BillableWeightKg).HasPrecision(14, 3);
        b.Property(c => c.BasicFreight).HasPrecision(14, 2);
        b.Property(c => c.OtherCharges).HasPrecision(14, 2);
        b.Property(c => c.DetentionAmount).HasPrecision(14, 2);
        b.Property(c => c.DetentionOverride).HasPrecision(14, 2);
        b.Property(c => c.DetentionOverrideReason).HasMaxLength(500);
        b.Property(c => c.ReviewNote).HasMaxLength(500);
        b.Property(c => c.Holds).HasJson();
        b.Property(c => c.Lines).HasJson();
        b.Property(c => c.Trace).HasJson();
        b.Property(c => c.Detention).HasJson();
        b.Property(c => c.Input).HasJson();
        b.HasIndex(c => new { c.TenantId, c.LrId, c.CalcVersion }).IsUnique();
        b.HasIndex(c => new { c.TenantId, c.CustomerId, c.Status });
        b.HasIndex(c => new { c.TenantId, c.InvoiceId });
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.Number).HasMaxLength(30).IsRequired();
        b.Property(i => i.Series).HasMaxLength(12).IsRequired();
        b.Property(i => i.FinancialYear).HasMaxLength(8).IsRequired();
        b.Property(i => i.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(i => i.CustomerGstin).HasMaxLength(15);
        b.Property(i => i.BillingAddress).HasMaxLength(500);
        b.Property(i => i.CustomerReference).HasMaxLength(100);
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(i => i.Submission).HasConversion<string>().HasMaxLength(24);
        b.Property(i => i.TaxMode).HasConversion<string>().HasMaxLength(16);
        b.Property(i => i.TaxRatePercent).HasPrecision(6, 2);
        b.Property(i => i.TaxNote).HasMaxLength(300);
        b.Property(i => i.Sac).HasMaxLength(12);
        b.Property(i => i.Subtotal).HasPrecision(14, 2);
        b.Property(i => i.Cgst).HasPrecision(14, 2);
        b.Property(i => i.Sgst).HasPrecision(14, 2);
        b.Property(i => i.Igst).HasPrecision(14, 2);
        b.Property(i => i.GrandTotal).HasPrecision(14, 2);
        b.Property(i => i.AmountPaid).HasPrecision(14, 2);
        b.Property(i => i.CancelReason).HasMaxLength(500);
        b.Property(i => i.Notes).HasMaxLength(500);
        b.Ignore(i => i.Outstanding);
        b.Ignore(i => i.PaymentState);
        b.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(i => i.Submissions).WithOne().HasForeignKey(s => s.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(i => new { i.TenantId, i.Number }).IsUnique();
        b.HasIndex(i => new { i.TenantId, i.CustomerId, i.Status });
        b.HasIndex(i => new { i.TenantId, i.InvoiceDate });
        b.HasIndex(i => new { i.TenantId, i.DueDate });
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> b)
    {
        b.ToTable("invoice_lines");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.LrNumber).HasMaxLength(30).IsRequired();
        b.Property(l => l.Description).HasMaxLength(300).IsRequired();
        b.Property(l => l.BasicFreight).HasPrecision(14, 2);
        b.Property(l => l.Detention).HasPrecision(14, 2);
        b.Property(l => l.OtherCharges).HasPrecision(14, 2);
        b.Property(l => l.Taxable).HasPrecision(14, 2);
        b.Property(l => l.Cgst).HasPrecision(14, 2);
        b.Property(l => l.Sgst).HasPrecision(14, 2);
        b.Property(l => l.Igst).HasPrecision(14, 2);
        b.HasIndex(l => new { l.TenantId, l.LrId });
        b.HasIndex(l => new { l.TenantId, l.InvoiceId });
    }
}

internal sealed class InvoiceSubmissionConfiguration : IEntityTypeConfiguration<InvoiceSubmission>
{
    public void Configure(EntityTypeBuilder<InvoiceSubmission> b)
    {
        b.ToTable("invoice_submissions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(s => s.PortalReference).HasMaxLength(100);
        b.Property(s => s.DocumentReference).HasMaxLength(200);
        b.Property(s => s.RejectionReason).HasMaxLength(500);
        b.Property(s => s.Notes).HasMaxLength(500);
        b.HasIndex(s => new { s.TenantId, s.InvoiceId, s.Attempt }).IsUnique();
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Amount).HasPrecision(14, 2);
        b.Property(p => p.Mode).HasMaxLength(30);
        b.Property(p => p.Reference).HasMaxLength(100);
        b.Property(p => p.Remarks).HasMaxLength(500);
        b.HasIndex(p => new { p.TenantId, p.InvoiceId });
        b.HasIndex(p => new { p.TenantId, p.CustomerId, p.ReceivedOn });
    }
}

internal sealed class InvoiceSeriesConfiguration : IEntityTypeConfiguration<InvoiceSeries>
{
    public void Configure(EntityTypeBuilder<InvoiceSeries> b)
    {
        b.ToTable("invoice_series");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Series).HasMaxLength(12).IsRequired();
        b.Property(s => s.FinancialYear).HasMaxLength(8).IsRequired();
        b.HasIndex(s => new { s.TenantId, s.Series, s.FinancialYear }).IsUnique();
    }
}

internal sealed class OpsSettingConfiguration : IEntityTypeConfiguration<OpsSetting>
{
    public void Configure(EntityTypeBuilder<OpsSetting> b)
    {
        b.ToTable("settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Key).HasMaxLength(40).IsRequired();
        b.Property(s => s.ValueJson).HasColumnType("text").IsRequired();
        b.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
    }
}
