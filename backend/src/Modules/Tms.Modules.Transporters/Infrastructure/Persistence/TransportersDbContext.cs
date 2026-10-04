using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Infrastructure.Persistence;

public sealed class TransportersDbContext(DbContextOptions<TransportersDbContext> options, ICurrentUser currentUser, IFieldEncryptor encryptor)
    : TmsDbContext(options, currentUser)
{
    public const string Schema = "transporters";

    public DbSet<Transporter> Transporters => Set<Transporter>();

    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Driver> Drivers => Set<Driver>();

    public DbSet<ComplianceDocument> Documents => Set<ComplianceDocument>();

    public DbSet<SequenceCounter> Sequences => Set<SequenceCounter>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportersDbContext).Assembly);

        // Bank account numbers are encrypted at rest (random nonce, so the column is deliberately not indexable).
        modelBuilder.Entity<Transporter>().Property(t => t.BankAccountNumber)
            .HasMaxLength(160)
            .HasConversion(
                v => v == null ? null : encryptor.Encrypt(v),
                v => v == null ? null : encryptor.Decrypt(v));
    }
}
