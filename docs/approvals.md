# Approval engine — how to use it from a business module

Approvals is a separate module (`Tms.Modules.Approvals`). Business modules never reference it directly; they use two
contracts in `Tms.SharedKernel.Contracts`.

## 1. Register your document type and approver permission
```csharp
services.AddSingleton(new ApprovalDocumentType("freight_bill", "Freight bill"));
services.AddSingleton(new PermissionDefinition("freight_bill.approve", "Billing", "Approve freight bills"));
```
Built-in types (transporter onboarding, freight contract, spot rate, freight bill, claim) are pre-registered in
`ApprovalsModule.BuiltInDocumentTypes`; move each to its owning module when that module is built.

## 2. Submit when a document needs approval
```csharp
var result = await approvalGateway.SubmitAsync(new SubmitApproval("freight_bill", bill.Id, $"Bill {bill.Number}", bill.Total), ct);
// result.Value.Status == Approved  → no step applied to this amount; proceed immediately
// result.Error.Code == "approvals.no_policy" → the tenant has not configured a policy; surface this to the user
```
Keep your document in a "pending approval" state until the outcome arrives.

## 3. React to the outcome
```csharp
internal sealed class BillApprovalHandler : IDomainEventHandler<ApprovalCompleted>
{
    public Task HandleAsync(ApprovalCompleted e, CancellationToken ct) =>
        e.DocumentType != "freight_bill" ? Task.CompletedTask : /* mark bill Approved / Rejected / Cancelled */;
}
```
Register with `services.AddDomainEventHandlers(typeof(YourModule).Assembly)`. The event fires once per request, after the
decision is committed, and also for requests that auto-approve on submit.

## Rules the engine enforces (so you don't have to)
- Steps run in order; a step applies when `amount >= MinAmount` (no threshold = always).
- The requester can never decide their own request; nobody can decide two steps of one request.
- A step is decided by anyone holding its permission, or by an active delegate of such a person.
- Policies are snapshotted per request: editing a policy never changes requests already in flight.
- No active policy ⇒ submission is refused (never silently approved).
- Every submit/decision/cancel/delegation is in the audit trail.

## Dev/test shortcut
`POST /api/v1/dev/approvals/submit` (Development and Testing environments only) submits a request through the gateway,
so the inbox UI can be exercised before document modules exist.
