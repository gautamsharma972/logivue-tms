using Microsoft.EntityFrameworkCore;
using Tms.Modules.Approvals.Domain;
using Tms.Modules.Approvals.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Application.Policies;

internal static class PolicyMapping
{
    public static PolicyDto ToDto(this ApprovalPolicy policy, string typeName) =>
        new(policy.Id, policy.DocumentType, typeName, true, policy.IsActive,
            policy.Steps.Select(s => new PolicyStepDto(s.Name, s.RequiredPermission, s.MinAmount)).ToList(), policy.Version);
}

internal sealed class ListPoliciesHandler(ApprovalsDbContext db, DocumentTypeCatalog documentTypes)
{
    public async Task<Result<IReadOnlyList<PolicyDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var policies = (await db.Policies.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(p => p.DocumentType);

        IReadOnlyList<PolicyDto> rows = documentTypes.All
            .Select(t => policies.TryGetValue(t.Code, out var p)
                ? p.ToDto(t.Name)
                : new PolicyDto(null, t.Code, t.Name, false, false, [], null))
            .ToList();
        return Result.Success(rows);
    }
}

internal sealed class SavePolicyHandler(
    ApprovalsDbContext db,
    ICurrentUser currentUser,
    DocumentTypeCatalog documentTypes,
    IEnumerable<PermissionDefinition> permissions)
{
    public async Task<Result<PolicyDto>> HandleAsync(string documentType, SavePolicyRequest request, CancellationToken cancellationToken)
    {
        if (documentTypes.Find(documentType) is not { } type)
        {
            return Error.NotFound("approvals.unknown_document_type", $"'{documentType}' is not a document type that supports approval.");
        }

        var known = permissions.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        if (request.Steps.FirstOrDefault(s => !known.Contains(s.RequiredPermission.Trim())) is { } bad)
        {
            return Error.Validation("approvals.unknown_permission", $"Step '{bad.Name}' requires unknown permission '{bad.RequiredPermission}'.");
        }

        var steps = request.Steps.Select(s => new PolicyStep(s.Name, s.RequiredPermission, s.MinAmount)).ToList();
        var policy = await db.Policies.FirstOrDefaultAsync(p => p.DocumentType == documentType, cancellationToken);

        if (policy is null)
        {
            var created = ApprovalPolicy.Create(currentUser.TenantId!.Value, documentType, request.IsActive, steps);
            if (created.IsFailure)
            {
                return created.Error;
            }

            policy = created.Value;
            db.Policies.Add(policy);
        }
        else
        {
            if (request.Version is null)
            {
                return Error.Validation("approvals.version_required", "The current version of the policy is required.");
            }

            db.Entry(policy).Property(p => p.Version).OriginalValue = request.Version.Value;
            var updated = policy.Update(request.IsActive, steps);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return policy.ToDto(type.Name);
    }
}

/// <summary>The permissions a policy step can require (the whole catalogue), for the policy editor.</summary>
internal sealed class ListStepPermissionsHandler(IEnumerable<PermissionDefinition> permissions)
{
    public Result<IReadOnlyList<PermissionDefinition>> Handle() =>
        Result.Success<IReadOnlyList<PermissionDefinition>>(
            permissions.OrderBy(p => p.Module, StringComparer.Ordinal).ThenBy(p => p.Code, StringComparer.Ordinal).ToList());
}
