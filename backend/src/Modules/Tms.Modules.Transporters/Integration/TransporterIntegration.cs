using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application.Transporters;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Messaging;

namespace Tms.Modules.Transporters.Integration;

/// <summary>Reacts to the approval engine: moves a transporter to Active / Rejected / back to Draft when its onboarding is decided.</summary>
internal sealed class TransporterApprovalSubscriber(TransportersDbContext db, TimeProvider clock) : IDomainEventHandler<ApprovalCompleted>
{
    public async Task HandleAsync(ApprovalCompleted domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.DocumentType != SubmitTransporterHandler.DocumentType)
        {
            return;
        }

        var transporter = await db.Transporters.FirstOrDefaultAsync(t => t.Id == domainEvent.DocumentId, cancellationToken);
        if (transporter is not null && transporter.ApplyApprovalOutcome(domainEvent.RequestId, domainEvent.Outcome, clock.GetUtcNow()))
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Answers other modules' "does this transporter exist (in my tenant)?" without exposing transporter data.</summary>
internal sealed class TransporterDirectory(TransportersDbContext db, Application.Fleet.VehicleTypeSeeder seeder) : ITransporterDirectory, IVehicleTypeDirectory
{
    public Task<bool> ExistsAsync(Guid transporterId, CancellationToken cancellationToken = default) =>
        db.Transporters.AnyAsync(t => t.Id == transporterId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, TransporterInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Transporters.AsNoTracking()
            .Where(t => wanted.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new TransporterInfo(t.Id, t.Code, t.LegalName, t.Status == Domain.TransporterStatus.Active, t.ContactPerson, t.Phone, t.Email, t.City), cancellationToken);
    }

    async Task<IReadOnlyDictionary<Guid, VehicleTypeInfo>> IVehicleTypeDirectory.GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await db.VehicleTypes.AsNoTracking()
            .Where(t => wanted.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new VehicleTypeInfo(t.Id, t.Code, t.Name, t.PayloadKg, t.VolumeCbm, t.IsActive, t.LengthM, t.WidthM, t.HeightM, t.AllowsHazardous, t.SupportsTemperatureControl), cancellationToken);
    }

    async Task<IReadOnlyList<VehicleTypeInfo>> IVehicleTypeDirectory.ListActiveAsync(CancellationToken cancellationToken)
    {
        await seeder.EnsureDefaultsAsync(cancellationToken);
        return await db.VehicleTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.PayloadKg).ThenBy(t => t.Name)
            .Select(t => new VehicleTypeInfo(t.Id, t.Code, t.Name, t.PayloadKg, t.VolumeCbm, t.IsActive, t.LengthM, t.WidthM, t.HeightM, t.AllowsHazardous, t.SupportsTemperatureControl))
            .ToListAsync(cancellationToken);
    }
}
