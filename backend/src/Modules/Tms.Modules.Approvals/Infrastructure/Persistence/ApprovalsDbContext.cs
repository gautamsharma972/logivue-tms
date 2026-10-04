using Microsoft.EntityFrameworkCore;
using Tms.Modules.Approvals.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Infrastructure.Persistence;

public sealed class ApprovalsDbContext(DbContextOptions<ApprovalsDbContext> options, ICurrentUser currentUser)
    : TmsDbContext(options, currentUser)
{
    public const string Schema = "approvals";

    public DbSet<ApprovalPolicy> Policies => Set<ApprovalPolicy>();

    public DbSet<ApprovalRequest> Requests => Set<ApprovalRequest>();

    public DbSet<Delegation> Delegations => Set<Delegation>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApprovalsDbContext).Assembly);
    }
}
