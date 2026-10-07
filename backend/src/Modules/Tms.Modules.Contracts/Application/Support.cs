using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Application;

/// <summary>Contracts are commercial data: internal staff only. Vendors do not see what other parties are paid.</summary>
internal sealed class ContractAccess(ICurrentUser user)
{
    private bool IsVendor => user.TransporterId is not null;

    private bool Has(string permission) => user.Permissions.Contains(permission);

    public bool CanRead => !IsVendor && (Has(ContractPermissions.Read) || Has(ContractPermissions.Manage));

    public bool CanManage => !IsVendor && Has(ContractPermissions.Manage);

    /// <summary>Keeping a rating against a shipment: a commercial duty, so managers and designated raters.</summary>
    public bool CanRate => !IsVendor && (Has(ContractPermissions.Rate) || Has(ContractPermissions.Manage));

    /// <summary>Someone asked to decide a contract needs to see that contract, whether or not they can browse the rest.</summary>
    public bool CanDecide => !IsVendor && (CanRead || Has(ContractPermissions.Approve));

    public bool CanOverride => !IsVendor && Has(ContractPermissions.Override);

    public bool CanVerify => !IsVendor && (Has(ContractPermissions.Verify) || Has(ContractPermissions.Approve));

    public static readonly Error Forbidden = Error.Forbidden("contracts.forbidden", "You are not allowed to do that.");

    public static readonly Error NotFound = Error.NotFound("contracts.not_found", "Contract not found.");
}

internal static class Support
{
    public static DateOnly TodayInIndia(this TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry };

    public static Result<Place> ToPlace(this PlaceDto dto) => Place.From(dto.Kind, dto.State, dto.City, dto.ZoneCode);

    public static PlaceDto ToDto(this Place place) => new(place.Kind, place.State, place.City, place.ZoneCode);

    /// <summary>When a revision becomes active, the contract it revises ends the day before. Caller saves.</summary>
    public static async Task SwitchOverAsync(this ContractsDbContext db, Contract revision, CancellationToken cancellationToken)
    {
        if (revision.Status != ContractStatus.Active || revision.RevisionOfId is not { } previousId)
        {
            return;
        }

        var previous = await db.Contracts.FirstOrDefaultAsync(c => c.Id == previousId, cancellationToken);
        previous?.SupersedeBy(revision.EffectiveFrom);
    }
}

/// <summary>Hands out gap-tolerant, never-repeating numbers per tenant (CN-00001, CN-00002…) using one atomic SQL upsert.</summary>
internal sealed class NumberSequence(ContractsDbContext db)
{
    public async Task<long> NextAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO contracts_sequences (tenant_id, name, `value`) VALUES ({tenantId}, {name}, 1) ON DUPLICATE KEY UPDATE `value` = `value` + 1",
            cancellationToken);

        var value = await db.Database
            .SqlQuery<long>($"SELECT `value` AS Value FROM contracts_sequences WHERE tenant_id = {tenantId} AND name = {name}")
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return value;
    }
}
