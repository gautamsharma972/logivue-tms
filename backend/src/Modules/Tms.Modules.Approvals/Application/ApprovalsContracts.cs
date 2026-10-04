using FluentValidation;
using Tms.Modules.Approvals.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Approvals.Application;

public sealed record PolicyStepDto(string Name, string RequiredPermission, decimal? MinAmount);

/// <summary>One row per known document type; <see cref="IsConfigured"/> is false until an admin saves a policy for it.</summary>
public sealed record PolicyDto(
    Guid? Id,
    string DocumentType,
    string DocumentTypeName,
    bool IsConfigured,
    bool IsActive,
    IReadOnlyList<PolicyStepDto> Steps,
    long? Version);

/// <param name="Version">Required when changing an existing policy (optimistic concurrency).</param>
public sealed record SavePolicyRequest(bool IsActive, IReadOnlyList<PolicyStepDto> Steps, long? Version);

public sealed record RequestStepDto(
    int Order,
    string Name,
    string RequiredPermission,
    StepStatus Status,
    Guid? DecidedBy,
    string? DecidedByName,
    Guid? OnBehalfOf,
    string? OnBehalfOfName,
    DateTimeOffset? DecidedAt,
    string? Comment);

public sealed record RequestDto(
    Guid Id,
    string DocumentType,
    string DocumentTypeName,
    Guid DocumentId,
    string Title,
    decimal? Amount,
    Guid RequesterId,
    string RequesterName,
    ApprovalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int? CurrentStepIndex,
    bool CanDecide,
    bool CanCancel,
    IReadOnlyList<RequestStepDto> Steps,
    long Version);

public sealed record RequestSummaryDto(
    Guid Id,
    string DocumentType,
    string DocumentTypeName,
    string Title,
    decimal? Amount,
    string RequesterName,
    ApprovalStatus Status,
    string? CurrentStepName,
    DateTimeOffset CreatedAt,
    bool CanDecide);

public sealed record DecisionRequest(string? Comment);

/// <param name="Scope"><c>inbox</c> (waiting for me), <c>mine</c> (I submitted) or <c>all</c> (needs approvals.read.all).</param>
public sealed record ListRequestsQuery(string? Scope, ApprovalStatus? Status, string? DocumentType, int Page = 1, int PageSize = 25);

public sealed record DelegationDto(
    Guid Id,
    Guid DelegatorId,
    string DelegatorName,
    Guid DelegateId,
    string DelegateName,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string? Reason,
    bool IsActive,
    long Version);

public sealed record DelegationsDto(IReadOnlyList<DelegationDto> Given, IReadOnlyList<DelegationDto> Received);

public sealed record CreateDelegationRequest(Guid DelegateId, DateTimeOffset ValidFrom, DateTimeOffset ValidTo, string? Reason);

internal sealed class SavePolicyRequestValidator : AbstractValidator<SavePolicyRequest>
{
    public SavePolicyRequestValidator()
    {
        RuleFor(x => x.Steps).NotNull().Must(s => s is { Count: >= 1 and <= ApprovalPolicy.MaxSteps })
            .WithMessage($"A policy needs between 1 and {ApprovalPolicy.MaxSteps} steps.");
        RuleForEach(x => x.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.Name).NotEmpty().MaximumLength(100);
            step.RuleFor(s => s.RequiredPermission).NotEmpty().MaximumLength(128);
            step.RuleFor(s => s.MinAmount).GreaterThanOrEqualTo(0).When(s => s.MinAmount is not null);
        });
    }
}

internal sealed class DecisionRequestValidator : AbstractValidator<DecisionRequest>
{
    public DecisionRequestValidator() => RuleFor(x => x.Comment).MaximumLength(1000);
}

internal sealed class CreateDelegationRequestValidator : AbstractValidator<CreateDelegationRequest>
{
    public CreateDelegationRequestValidator()
    {
        RuleFor(x => x.DelegateId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}
