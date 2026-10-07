using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application.Masters;

internal sealed class ListZonesHandler(ContractsDbContext db, ContractAccess access)
{
    public async Task<Result<IReadOnlyList<ZoneDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var zones = await db.Zones.AsNoTracking().OrderBy(z => z.Code).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ZoneDto>>(zones.Select(z => z.ToDto()).ToList());
    }
}

internal sealed class SaveZoneHandler(ContractsDbContext db, ContractAccess access, ICurrentUser currentUser)
{
    public async Task<Result<ZoneDto>> CreateAsync(SaveZoneRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var created = Zone.Create(currentUser.TenantId!.Value, request.Code, request.Name, request.Members);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var taken = Error.Conflict("zones.code_exists", $"A zone with code {created.Value.Code} already exists.");
        if (await db.Zones.AnyAsync(z => z.Code == created.Value.Code, cancellationToken))
        {
            return taken;
        }

        db.Zones.Add(created.Value);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return taken;
        }

        return created.Value.ToDto();
    }

    public async Task<Result<ZoneDto>> UpdateAsync(Guid id, SaveZoneRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        if (request.Version is null)
        {
            return Error.Validation("zones.version_required", "The current version of the zone is required.");
        }

        var zone = await db.Zones.FirstOrDefaultAsync(z => z.Id == id, cancellationToken);
        if (zone is null)
        {
            return Error.NotFound("zones.not_found", "Zone not found.");
        }

        db.Entry(zone).Property(z => z.Version).OriginalValue = request.Version.Value;
        var updated = zone.Update(request.Name, request.Members);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return zone.ToDto();
    }
}

internal sealed class ListDieselPricesHandler(ContractsDbContext db, ContractAccess access)
{
    public async Task<Result<IReadOnlyList<DieselPriceDto>>> HandleAsync(ListDieselPricesQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var prices = db.DieselPrices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            var region = query.Region.Trim().ToUpperInvariant();
            prices = prices.Where(p => p.Region == region);
        }

        var rows = await prices.OrderBy(p => p.Region).ThenByDescending(p => p.EffectiveFrom).Take(1000).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<DieselPriceDto>>(rows.Select(p => p.ToDto()).ToList());
    }
}

internal sealed class AddDieselPriceHandler(ContractsDbContext db, ContractAccess access, ICurrentUser currentUser)
{
    public async Task<Result<DieselPriceDto>> HandleAsync(AddDieselPriceRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return ContractAccess.Forbidden;
        }

        var created = DieselPrice.Create(currentUser.TenantId!.Value, request.Region, request.EffectiveFrom, request.PricePerLitre, request.Source);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var exists = Error.Conflict("diesel.price_exists", $"A price for {created.Value.Region} from {created.Value.EffectiveFrom:dd MMM yyyy} is already recorded.");
        if (await db.DieselPrices.AnyAsync(p => p.Region == created.Value.Region && p.EffectiveFrom == created.Value.EffectiveFrom, cancellationToken))
        {
            return exists;
        }

        db.DieselPrices.Add(created.Value);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return exists;
        }

        return created.Value.ToDto();
    }
}

/// <summary>Vehicle types for the rate editor and quote forms, so contract users do not need transporter permissions.</summary>
internal sealed class ListVehicleTypesHandler(ContractAccess access, IVehicleTypeDirectory vehicleTypes)
{
    public async Task<Result<IReadOnlyList<VehicleTypeInfo>>> HandleAsync(CancellationToken cancellationToken) =>
        access.CanRead
            ? Result.Success(await vehicleTypes.ListActiveAsync(cancellationToken))
            : ContractAccess.Forbidden;
}
