using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Workflow;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Workflow;

/// <summary>Onboarding, approval, suspension and blacklisting. Every action is recorded in the approval history.</summary>
public interface IOnboardingService
{
    Task<TransporterDetail> SubmitAsync(long transporterId, CommentsRequest request, CancellationToken cancellationToken = default);

    Task<TransporterDetail> ApproveStageAsync(long transporterId, CommentsRequest request, CancellationToken cancellationToken = default);

    Task<TransporterDetail> RejectAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<TransporterDetail> SuspendAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<TransporterDetail> ActivateAsync(long transporterId, CommentsRequest request, CancellationToken cancellationToken = default);

    Task<TransporterDetail> DeactivateAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<TransporterDetail> BlacklistAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalActionDto>> GetHistoryAsync(long transporterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OnboardingStepDto>> GetStepsAsync(CancellationToken cancellationToken = default);
}

public sealed class OnboardingService(
    IRepository<Transporter> transporters,
    IRepository<TransporterApprovalAction> actions,
    IRepository<OnboardingStep> steps,
    IRepository<TransporterContact> contacts,
    IComplianceService compliance,
    ITransporterQueries queries,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<CommentsRequest> commentsValidator,
    IValidator<ReasonRequest> reasonValidator,
    ILogger<OnboardingService> logger) : IOnboardingService
{
    private static readonly TransporterStatus[] SubmittableFrom = [TransporterStatus.Draft, TransporterStatus.Rejected];

    public async Task<TransporterDetail> SubmitAsync(long transporterId, CommentsRequest request, CancellationToken cancellationToken = default)
    {
        await commentsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        WorkflowRules.EnsureAllowedFrom(transporter.Status, "submitted", SubmittableFrom);
        WorkflowRules.EnsureRole(currentUser, Roles.TransportExecutive, "submit a transporter");

        var missing = await MissingSubmissionDataAsync(transporter, cancellationToken);
        if (missing.Count > 0)
        {
            throw new BusinessRuleException("The transporter is incomplete and cannot be submitted.", "TRANSPORTER_INCOMPLETE", missing);
        }

        return await MoveAsync(transporter, ApprovalActionType.Submit, TransporterStatus.Submitted, comments: request.Comments, reason: null, cancellationToken);
    }

    public async Task<TransporterDetail> ApproveStageAsync(long transporterId, CommentsRequest request, CancellationToken cancellationToken = default)
    {
        await commentsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var stepList = await ActiveStepsAsync(cancellationToken);
        var current = transporter.Status;
        if (current != TransporterStatus.Submitted && !WorkflowRules.IsReviewStage(current, stepList))
        {
            throw WorkflowRules.Illegal(current, "approved at this stage");
        }

        WorkflowRules.EnsureRole(currentUser, WorkflowRules.ApproverRole(current, stepList), "approve this stage");

        if (WorkflowRules.IsReviewStage(current, stepList))
        {
            var report = await compliance.GetReportAsync(transporterId, cancellationToken);
            if (report.ApprovalBlocked)
            {
                throw new BusinessRuleException("Approval is blocked by compliance.", "APPROVAL_BLOCKED_BY_COMPLIANCE",
                    report.Items.Where(i => i.BlocksApproval)
                        .Select(i => new ApiErrorDetail(i.DocumentTypeCode, i.Message))
                        .ToList());
            }
        }

        var next = WorkflowRules.NextStage(current, stepList);
        return await MoveAsync(transporter, ApprovalActionType.Advance, next, comments: request.Comments, reason: null, cancellationToken);
    }

    public async Task<TransporterDetail> RejectAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var stepList = await ActiveStepsAsync(cancellationToken);
        var current = transporter.Status;
        if (current != TransporterStatus.Submitted && !WorkflowRules.IsReviewStage(current, stepList))
        {
            throw WorkflowRules.Illegal(current, "rejected");
        }

        WorkflowRules.EnsureRole(currentUser, WorkflowRules.ApproverRole(current, stepList), "reject this stage");

        return await MoveAsync(transporter, ApprovalActionType.Reject, TransporterStatus.Rejected, request.Comments, request.Reason, cancellationToken);
    }

    public async Task<TransporterDetail> SuspendAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        WorkflowRules.EnsureAllowedFrom(transporter.Status, "suspended", TransporterStatus.Active);
        WorkflowRules.EnsureRole(currentUser, Roles.TransportManager, "suspend a transporter");

        return await MoveAsync(transporter, ApprovalActionType.Suspend, TransporterStatus.Suspended, request.Comments, request.Reason, cancellationToken);
    }

    public async Task<TransporterDetail> ActivateAsync(long transporterId, CommentsRequest request, CancellationToken cancellationToken = default)
    {
        await commentsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        WorkflowRules.EnsureAllowedFrom(transporter.Status, "activated", TransporterStatus.Approved, TransporterStatus.Suspended);
        WorkflowRules.EnsureRole(currentUser, Roles.TransportManager, "activate a transporter");

        var report = await compliance.GetReportAsync(transporterId, cancellationToken);
        if (report.ApprovalBlocked)
        {
            throw new BusinessRuleException("Activation is blocked by compliance.", "ACTIVATION_BLOCKED_BY_COMPLIANCE",
                report.Items.Where(i => i.BlocksApproval).Select(i => new ApiErrorDetail(i.DocumentTypeCode, i.Message)).ToList());
        }

        return await MoveAsync(transporter, ApprovalActionType.Activate, TransporterStatus.Active, request.Comments, null, cancellationToken);
    }

    public async Task<TransporterDetail> DeactivateAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        WorkflowRules.EnsureAllowedFrom(transporter.Status, "deactivated",
            TransporterStatus.Draft, TransporterStatus.Rejected, TransporterStatus.Approved, TransporterStatus.Active, TransporterStatus.Suspended);
        WorkflowRules.EnsureRole(currentUser, Roles.TransportManager, "deactivate a transporter");

        return await MoveAsync(transporter, ApprovalActionType.Deactivate, TransporterStatus.Deactivated, request.Comments, request.Reason, cancellationToken);
    }

    public async Task<TransporterDetail> BlacklistAsync(long transporterId, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        WorkflowRules.EnsureRole(currentUser, Roles.TransportManager, "blacklist a transporter");

        return await MoveAsync(transporter, ApprovalActionType.Blacklist, TransporterStatus.Blacklisted, request.Comments, request.Reason, cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovalActionDto>> GetHistoryAsync(long transporterId, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);

        return (await actions.ListAsync(a => a.TransporterId == transporterId, cancellationToken))
            .OrderBy(a => a.ActionAt).ThenBy(a => a.Id)
            .Select(a => new ApprovalActionDto(a.Id, a.Action, a.FromStatus, a.ToStatus, a.ActorUserId, a.ActionAt, a.Comments, a.Reason))
            .ToList();
    }

    public async Task<IReadOnlyList<OnboardingStepDto>> GetStepsAsync(CancellationToken cancellationToken = default) =>
        (await ActiveStepsAsync(cancellationToken))
        .Select(s => new OnboardingStepDto(s.Id, s.Sequence, s.Status, s.Name, s.RequiredRole))
        .ToList();

    private async Task<TransporterDetail> MoveAsync(
        Transporter transporter,
        ApprovalActionType action,
        TransporterStatus to,
        string? comments,
        string? reason,
        CancellationToken cancellationToken)
    {
        var from = transporter.Status;
        var now = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        actions.Add(new TransporterApprovalAction
        {
            TransporterId = transporter.Id,
            Action = action,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = currentUser.UserId,
            ActionAt = now,
            Comments = Clean(comments),
            Reason = Clean(reason)
        });

        transporter.Status = to;
        transporter.UpdatedAt = now;
        transporter.UpdatedBy = currentUser.UserId;

        await audit.RecordAsync(new AuditEntry("Transporter", transporter.Id.ToString(), $"Workflow{action}",
            OldValueJson: AuditJson.Serialize(new { Status = from }),
            NewValueJson: AuditJson.Serialize(new { Status = to }),
            Reason: Clean(reason) ?? Clean(comments)), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Transporter {TransporterId} workflow {Action}: {From} -> {To}", transporter.Id, action, from, to);

        return await queries.GetTransporterAsync(transporter.Id, cancellationToken)
            ?? throw new NotFoundException($"Transporter {transporter.Id} was not found.", "TRANSPORTER_NOT_FOUND");
    }

    private async Task<List<ApiErrorDetail>> MissingSubmissionDataAsync(Transporter transporter, CancellationToken cancellationToken)
    {
        var missing = new List<ApiErrorDetail>();

        if (string.IsNullOrWhiteSpace(transporter.LegalName))
        {
            missing.Add(new ApiErrorDetail(nameof(Transporter.LegalName), "Legal name is required."));
        }

        if (string.IsNullOrWhiteSpace(transporter.Pan))
        {
            missing.Add(new ApiErrorDetail(nameof(Transporter.Pan), "PAN is required before submission."));
        }

        if (string.IsNullOrWhiteSpace(transporter.Gstin))
        {
            missing.Add(new ApiErrorDetail(nameof(Transporter.Gstin), "GSTIN is required before submission."));
        }

        if (string.IsNullOrWhiteSpace(transporter.PrimaryContactEmail) || string.IsNullOrWhiteSpace(transporter.PrimaryContactPhone))
        {
            missing.Add(new ApiErrorDetail("PrimaryContact", "A primary contact email and phone are required."));
        }

        if (!await contacts.AnyAsync(c => c.TransporterId == transporter.Id && c.IsActive, cancellationToken))
        {
            missing.Add(new ApiErrorDetail("Contacts", "At least one active contact is required."));
        }

        return missing;
    }

    private async Task<IReadOnlyList<OnboardingStep>> ActiveStepsAsync(CancellationToken cancellationToken) =>
        (await steps.ListAsync(s => s.IsActive, cancellationToken)).OrderBy(s => s.Sequence).ToList();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
