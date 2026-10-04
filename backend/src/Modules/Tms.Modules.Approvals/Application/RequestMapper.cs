using Tms.Modules.Approvals.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Approvals.Application;

internal sealed class RequestMapper(IUserDirectory directory, DocumentTypeCatalog documentTypes)
{
    private static IEnumerable<Guid> PeopleIn(ApprovalRequest r) =>
        r.Steps.SelectMany(s => new[] { s.DecidedBy, s.OnBehalfOf }).Concat([r.RequesterId]).OfType<Guid>();

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<ApprovalRequest> requests, CancellationToken cancellationToken) =>
        await directory.GetDisplayNamesAsync(requests.SelectMany(PeopleIn), cancellationToken);

    public RequestDto ToDto(ApprovalRequest r, IReadOnlyDictionary<Guid, string> names, AuthorityContext? authority, bool canCancel)
    {
        string? Name(Guid? id) => id is { } g && names.TryGetValue(g, out var n) ? n : null;

        return new RequestDto(
            r.Id,
            r.DocumentType,
            documentTypes.NameOf(r.DocumentType),
            r.DocumentId,
            r.Title,
            r.Amount,
            r.RequesterId,
            Name(r.RequesterId) ?? "Unknown user",
            r.Status,
            r.CreatedAt,
            r.CompletedAt,
            r.CurrentStepIndex,
            authority?.Resolve(r).Allowed ?? false,
            canCancel,
            r.Steps.Select(s => new RequestStepDto(
                s.Order, s.Name, s.RequiredPermission, s.Status, s.DecidedBy, Name(s.DecidedBy), s.OnBehalfOf, Name(s.OnBehalfOf), s.DecidedAt, s.Comment)).ToList(),
            r.Version);
    }

    public RequestSummaryDto ToSummary(ApprovalRequest r, IReadOnlyDictionary<Guid, string> names, AuthorityContext? authority) =>
        new(
            r.Id,
            r.DocumentType,
            documentTypes.NameOf(r.DocumentType),
            r.Title,
            r.Amount,
            names.TryGetValue(r.RequesterId, out var requester) ? requester : "Unknown user",
            r.Status,
            r.CurrentStep?.Name,
            r.CreatedAt,
            authority?.Resolve(r).Allowed ?? false);
}
