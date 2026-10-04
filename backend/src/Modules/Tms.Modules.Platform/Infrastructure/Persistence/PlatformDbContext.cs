using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ICurrentUser currentUser)
    : TmsDbContext(options, currentUser)
{
    public const string Schema = "platform";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<SequenceRow> Sequences => Set<SequenceRow>();

    protected override bool OwnsSharedTables => true;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);
    }
}
