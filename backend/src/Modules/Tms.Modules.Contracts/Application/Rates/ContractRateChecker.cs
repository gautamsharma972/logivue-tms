using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;

namespace Tms.Modules.Contracts.Application;

/// <summary>
/// Puts a contract's rates in front of the validator together with what is already in force for the same transporter, so the same checks run when a contract is submitted,
/// when someone asks for a validation, and when an import is previewed.
/// </summary>
internal sealed class ContractRateChecker(ContractsDbContext db)
{
    /// <summary>Checks the contract's own rates (its cards and DPH rules must be loaded).</summary>
    public async Task<RateValidationResult> CheckAsync(Contract contract, CancellationToken cancellationToken)
    {
        var rows = contract.RateCards.OrderBy(c => c.Code, StringComparer.Ordinal)
            .Select((c, i) => new RateToCheck(i + 1, c.ToSpec(), c.ValidFrom ?? contract.EffectiveFrom, c.ValidTo ?? contract.EffectiveTo, contract.Reference)).ToList();
        return await CheckRowsAsync(rows, contract.TransporterId, contract.Number, contract.EffectiveFrom, contract.EffectiveTo, contract.EffectiveServices,
            contract.DphRules.Select(r => r.Code.ToUpperInvariant()).ToHashSet(), cancellationToken);
    }

    public async Task<RateValidationResult> CheckRowsAsync(
        IReadOnlyList<RateToCheck> rows, Guid? transporterId, string? ownNumber, DateOnly from, DateOnly to, IReadOnlyCollection<ContractType> services, IReadOnlySet<string> dphCodes, CancellationToken cancellationToken,
        IReadOnlyList<RateToCheck>? alsoInForce = null)
    {
        var inForce = new List<RateToCheck>(alsoInForce ?? []);
        if (transporterId is { } transporter && rows.Count > 0)
        {
            // Other contracts of the same transporter that are or will be in force while this one is. A revision of the same contract replaces its predecessor, so it is not compared with it.
            var others = await db.Contracts.AsNoTracking()
                .Where(c => c.TransporterId == transporter && c.Number != ownNumber
                            && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Suspended) && c.EffectiveTo >= from && c.EffectiveFrom <= to)
                .Include(c => c.RateCards)
                .ToListAsync(cancellationToken);
            foreach (var other in others)
            {
                inForce.AddRange(other.RateCards.Select(c => new RateToCheck(0, c.ToSpec(), c.ValidFrom ?? other.EffectiveFrom, c.ValidTo ?? other.EffectiveTo, other.Reference)));
            }
        }

        return RateValidator.Validate(rows, from, to, services, dphCodes, inForce);
    }
}
