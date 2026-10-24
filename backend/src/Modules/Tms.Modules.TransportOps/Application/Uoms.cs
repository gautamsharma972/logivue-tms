using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.TransportOps.Domain;
using Tms.Modules.TransportOps.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record UomDto(Guid? Id, string Code, string Name, bool IsActive, bool IsDefault);

public sealed record SaveUomRequest(string Code, string Name, bool IsActive = true);

internal sealed class SaveUomRequestValidator : AbstractValidator<SaveUomRequest>
{
    public SaveUomRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(12).Matches("^[A-Za-z0-9_.-]+$").WithMessage("Use letters, digits, - . and _ only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
    }
}

/// <summary>
/// The units an LR line can use. A tenant that has not set up its own list gets the built-in defaults; the first time it adds one, the defaults are copied in so nothing disappears.
/// </summary>
internal sealed class UomHandler(TransportOpsDbContext db, OpsAccess access)
{
    public async Task<Result<IReadOnlyList<UomDto>>> ListAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var own = await db.Uoms.AsNoTracking().OrderBy(u => u.Code).ToListAsync(cancellationToken);
        IReadOnlyList<UomDto> all = own.Count == 0
            ? UnitsOfMeasure.Defaults.Select(d => new UomDto(null, d.Code, d.Name, true, true)).ToList()
            : own.Select(u => new UomDto(u.Id, u.Code, u.Name, u.IsActive, false)).ToList();
        return Result<IReadOnlyList<UomDto>>.Success(activeOnly ? all.Where(u => u.IsActive).ToList() : all);
    }

    /// <summary>The unit codes an LR may use right now.</summary>
    public async Task<HashSet<string>> ActiveCodesAsync(CancellationToken cancellationToken)
    {
        var codes = await db.Uoms.AsNoTracking().Where(u => u.IsActive).Select(u => u.Code).ToListAsync(cancellationToken);
        return codes.Count > 0 || await db.Uoms.AnyAsync(cancellationToken)
            ? codes.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : UnitsOfMeasure.Defaults.Select(d => d.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Result<UomDto>> CreateAsync(SaveUomRequest request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var tenant = access.TenantId!.Value;
        if (!await db.Uoms.AnyAsync(cancellationToken))
        {
            db.Uoms.AddRange(UnitsOfMeasure.Defaults.Select(d => UnitOfMeasure.Create(tenant, d.Code, d.Name)));
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Uoms.AnyAsync(u => u.Code == code, cancellationToken) || db.Uoms.Local.Any(u => u.Code == code))
        {
            return Error.Conflict("ops.uom_duplicate", $"The unit {code} already exists.");
        }

        var uom = UnitOfMeasure.Create(tenant, code, request.Name);
        if (!request.IsActive)
        {
            uom.Edit(request.Name, false);
        }

        db.Uoms.Add(uom);
        await db.SaveChangesAsync(cancellationToken);
        return new UomDto(uom.Id, uom.Code, uom.Name, uom.IsActive, false);
    }

    public async Task<Result<UomDto>> UpdateAsync(Guid id, SaveUomRequest request, CancellationToken cancellationToken)
    {
        var uom = await db.Uoms.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (uom is null)
        {
            return Error.NotFound("ops.uom_not_found", "Unit of measure not found.");
        }

        uom.Edit(request.Name, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);
        return new UomDto(uom.Id, uom.Code, uom.Name, uom.IsActive, false);
    }
}
