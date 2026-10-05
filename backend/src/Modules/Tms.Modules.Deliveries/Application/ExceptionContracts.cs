using FluentValidation;
using Microsoft.AspNetCore.Http;
using Tms.Modules.Deliveries.Domain;

namespace Tms.Modules.Deliveries.Application;

public sealed record ExceptionNoteDto(DateTimeOffset At, string Text, Guid? By);

public sealed record ExceptionAttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, string? Note, DateTimeOffset At, Guid? By);

public sealed class AttachToExceptionForm
{
    public IFormFile? File { get; init; }

    public string? Note { get; init; }
}

public sealed record ExceptionSummaryDto(
    Guid Id, string Number, Guid DeliveryId, string DeliveryNumber, string? CustomerName, string? TransporterReference, string? VehicleReference, Guid? PodId, ExceptionType Type,
    ExceptionSeverity Severity, ExceptionStatus Status, Guid? OwnerUserId, string? Department, DateTimeOffset RaisedAt, DateTimeOffset DueAt, bool Overdue, double AgeHours, string? ClaimReference);

public sealed record ExceptionDto(
    ExceptionSummaryDto Summary, string Description, string? RootCause, ResponsibleParty ResponsibleParty, string? ActionTaken, string? Resolution, decimal? FinancialImpact,
    DateTimeOffset? ResolvedAt, DateTimeOffset? EscalatedAt, IReadOnlyList<ExceptionNoteDto> Notes, long Version, IReadOnlyList<ExceptionAttachmentDto>? Attachments = null);

public sealed record ListExceptionsQuery(
    ExceptionType? Type = null, ExceptionStatus? Status = null, ExceptionSeverity? Severity = null, Guid? TransporterId = null, Guid? DeliveryId = null, bool? OpenOnly = null,
    bool? Overdue = null, int Page = 1, int PageSize = 25);

public sealed record RaiseExceptionRequest(Guid DeliveryId, ExceptionType Type, string Description, ExceptionSeverity? Severity);

public sealed record AssignExceptionRequest(Guid? OwnerUserId, string? Department, DateTimeOffset? DueAt, ExceptionSeverity? Severity);

public sealed record InvestigateExceptionRequest(string? RootCause, ResponsibleParty? ResponsibleParty);

public sealed record ResolveExceptionRequest(string Resolution, string? RootCause, ResponsibleParty ResponsibleParty, string? ActionTaken, decimal? FinancialImpact, string? ClaimReference);

public sealed record NoteRequest(string Text);

internal sealed class RaiseExceptionRequestValidator : AbstractValidator<RaiseExceptionRequest>
{
    public RaiseExceptionRequestValidator()
    {
        RuleFor(x => x.DeliveryId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
    }
}

internal sealed class AssignExceptionRequestValidator : AbstractValidator<AssignExceptionRequest>
{
    public AssignExceptionRequestValidator() => RuleFor(x => x.Department).MaximumLength(100);
}

internal sealed class ResolveExceptionRequestValidator : AbstractValidator<ResolveExceptionRequest>
{
    public ResolveExceptionRequestValidator()
    {
        RuleFor(x => x.Resolution).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.ResponsibleParty).IsInEnum();
    }
}

internal sealed class NoteRequestValidator : AbstractValidator<NoteRequest>
{
    public NoteRequestValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
}
