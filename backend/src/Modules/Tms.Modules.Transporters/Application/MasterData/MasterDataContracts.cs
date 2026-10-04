using FluentValidation;
using Tms.Modules.Transporters.Domain;

namespace Tms.Modules.Transporters.Application.MasterData;

public sealed record MasterEntryDto(string Code, string Name, bool IsActive, bool IsBuiltIn);

public sealed record SaveMasterItemRequest(string Code, string Name, bool IsActive = true);

/// <param name="Kind">The paper this rule is for.</param>
public sealed record DocumentRuleDto(
    DocumentKind Kind, string Label, OwnerKind Owner, bool IsMandatory, bool ExpiryRequired, int RenewalReminderDays, bool BlockWhenExpired, bool IsActive, bool IsCustomised);

public sealed record SaveDocumentRuleRequest(bool IsMandatory, bool ExpiryRequired, int RenewalReminderDays, bool BlockWhenExpired, bool IsActive);

internal sealed class SaveMasterItemRequestValidator : AbstractValidator<SaveMasterItemRequest>
{
    public SaveMasterItemRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(MasterItem.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

internal sealed class SaveDocumentRuleRequestValidator : AbstractValidator<SaveDocumentRuleRequest>
{
    public SaveDocumentRuleRequestValidator() => RuleFor(x => x.RenewalReminderDays).InclusiveBetween(0, DocumentPolicy.MaxReminderDays);
}
