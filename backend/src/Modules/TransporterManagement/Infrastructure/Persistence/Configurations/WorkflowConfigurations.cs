using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Workflow;
using LogiVue.Tms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Configurations;

internal sealed class OnboardingStepConfiguration : IEntityTypeConfiguration<OnboardingStep>
{
    public void Configure(EntityTypeBuilder<OnboardingStep> b)
    {
        b.ToTable("tm_onboarding_steps");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequiredRole).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Sequence).IsUnique();
        b.HasIndex(x => x.Status).IsUnique();

        b.HasData(
            new OnboardingStep { Id = 1, Sequence = 1, Status = TransporterStatus.DocumentVerification, Name = "Document Verification", RequiredRole = Roles.ComplianceUser },
            new OnboardingStep { Id = 2, Sequence = 2, Status = TransporterStatus.OperationsReview, Name = "Operations Review", RequiredRole = Roles.OperationsUser },
            new OnboardingStep { Id = 3, Sequence = 3, Status = TransporterStatus.CommercialReview, Name = "Commercial Review", RequiredRole = Roles.TransportManager },
            new OnboardingStep { Id = 4, Sequence = 4, Status = TransporterStatus.FinanceReview, Name = "Finance Review", RequiredRole = Roles.FinanceUser });
    }
}

internal sealed class ApprovalActionConfiguration : IEntityTypeConfiguration<TransporterApprovalAction>
{
    public void Configure(EntityTypeBuilder<TransporterApprovalAction> b)
    {
        b.ToTable("tm_transporter_approval_actions");
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ActorUserId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Comments).HasMaxLength(1000);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => new { x.TransporterId, x.ActionAt });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}
