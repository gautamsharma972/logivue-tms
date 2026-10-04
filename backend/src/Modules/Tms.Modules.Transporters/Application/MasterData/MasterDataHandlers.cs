using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.MasterData;

/// <summary>The master lists (transporter types, capabilities) and the document rules. Everyone in the module reads them; managing needs <c>transporters.manage</c>.</summary>
internal sealed class MasterDataHandler(TransportersDbContext db, TransporterAccess access, ICurrentUser user, DocumentPolicyProvider provider)
{
    public async Task<Result<IReadOnlyList<MasterEntryDto>>> ListAsync(MasterKind kind, CancellationToken cancellationToken)
    {
        if (!access.CanUseModule)
        {
            return TransporterAccess.Forbidden;
        }

        return MasterCatalog.Merge(kind, await provider.ItemsAsync(cancellationToken)).Select(e => new MasterEntryDto(e.Code, e.Name, e.IsActive, e.IsBuiltIn)).ToList();
    }

    /// <summary>Adds an entry, or overrides a built-in one (rename or switch off) when the code matches.</summary>
    public async Task<Result<MasterEntryDto>> SaveAsync(MasterKind kind, SaveMasterItemRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageInternally || user.TenantId is not { } tenantId)
        {
            return TransporterAccess.Forbidden;
        }

        var code = MasterItem.NormaliseCode(request.Code);
        var existing = await db.MasterItems.FirstOrDefaultAsync(i => i.Kind == kind && i.Code == code, cancellationToken);
        if (existing is null)
        {
            var created = MasterItem.Create(tenantId, kind, code, request.Name);
            if (created.IsFailure)
            {
                return created.Error;
            }

            var inactive = request.IsActive ? Result.Success() : created.Value.Set(request.Name, false);
            if (inactive.IsFailure)
            {
                return inactive.Error;
            }

            db.MasterItems.Add(created.Value);
            existing = created.Value;
        }
        else
        {
            var changed = existing.Set(request.Name, request.IsActive);
            if (changed.IsFailure)
            {
                return changed.Error;
            }
        }

        // A capability that transporters still hold may be switched off (nobody new gets it) but the holders keep it, so history stays explainable.
        await db.SaveChangesAsync(cancellationToken);
        var builtIn = (kind == MasterKind.TransporterType ? MasterCatalog.BuiltInTypes : CapabilityCatalog.Items).Any(b => b.Code == existing.Code);
        return new MasterEntryDto(existing.Code, existing.Name, existing.IsActive, builtIn);
    }

    public async Task<Result<IReadOnlyList<DocumentRuleDto>>> ListRulesAsync(CancellationToken cancellationToken)
    {
        if (!access.CanUseModule)
        {
            return TransporterAccess.Forbidden;
        }

        var policy = await provider.GetAsync(cancellationToken);
        return Enum.GetValues<OwnerKind>().SelectMany(owner => ComplianceDocument.KindsFor(owner).Select(kind => ToDto(kind, owner, policy))).ToList();
    }

    public async Task<Result<DocumentRuleDto>> SaveRuleAsync(DocumentKind kind, SaveDocumentRuleRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageInternally || user.TenantId is not { } tenantId)
        {
            return TransporterAccess.Forbidden;
        }

        if (!Enum.IsDefined(kind))
        {
            return Error.NotFound("documents.rule_not_found", "No such kind of document.");
        }

        var values = new DocumentRuleValues(request.IsMandatory, request.ExpiryRequired, request.RenewalReminderDays, request.BlockWhenExpired, request.IsActive);
        var rule = await db.DocumentRules.FirstOrDefaultAsync(r => r.Kind == kind, cancellationToken);
        if (rule is null)
        {
            var created = DocumentRule.Create(tenantId, kind, values);
            if (created.IsFailure)
            {
                return created.Error;
            }

            db.DocumentRules.Add(created.Value);
        }
        else if (rule.Set(values) is { IsFailure: true } failed)
        {
            return failed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        var owner = Enum.GetValues<OwnerKind>().First(o => ComplianceDocument.KindsFor(o).Contains(kind));
        return ToDto(kind, owner, new DocumentPolicy(await db.DocumentRules.AsNoTracking().ToListAsync(cancellationToken)));
    }

    private static DocumentRuleDto ToDto(DocumentKind kind, OwnerKind owner, DocumentPolicy policy)
    {
        var v = policy.For(kind);
        return new DocumentRuleDto(kind, ComplianceEvaluator.Label(kind), owner, v.IsMandatory, v.ExpiryRequired, v.RenewalReminderDays, v.BlockWhenExpired, v.IsActive, policy.IsOverridden(kind));
    }
}
