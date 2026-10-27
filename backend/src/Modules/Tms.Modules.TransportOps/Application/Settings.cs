using FluentValidation;
using Tms.Modules.TransportOps.Domain;
using Tms.SharedKernel.India;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Application;

public sealed record OpsSettingsDto(ArrivalSetting Arrival, NumberingSetting Numbering, PodSetting Pod, InvoiceSetting Invoice, FleetSetting Fleet);

internal sealed class OpsSettingsDtoValidator : AbstractValidator<OpsSettingsDto>
{
    public OpsSettingsDtoValidator()
    {
        RuleFor(x => x.Arrival.MaxAccuracyM).InclusiveBetween(10, 1000);
        RuleFor(x => x.Arrival.TokenValidityDays).InclusiveBetween(1, 120);
        RuleFor(x => x.Numbering.Digits).InclusiveBetween(3, 10);
        foreach (var prefix in new Func<OpsSettingsDto, string>[] { x => x.Numbering.RequestPrefix, x => x.Numbering.LrPrefix, x => x.Numbering.TripPrefix })
        {
            RuleFor(x => prefix(x)).NotEmpty().MaximumLength(8).Matches("^[A-Za-z0-9]+$").WithMessage("Use letters and digits only.");
        }

        RuleFor(x => x.Pod.MaxFiles).InclusiveBetween(1, PodSetting.Ceiling);
        RuleFor(x => x.Pod.MaxBytes).InclusiveBetween(100 * 1024, 10 * 1024 * 1024);
        RuleFor(x => x.Invoice.SeriesPrefix).NotEmpty().MaximumLength(6).Matches("^[A-Za-z0-9]+$").WithMessage("Use up to 6 letters or digits (an invoice number is at most 16 characters).");
        RuleFor(x => x.Invoice.SupplierGstin).Must(g => IndianIdentifiers.IsValidGstin(g)).When(x => !string.IsNullOrWhiteSpace(x.Invoice.SupplierGstin)).WithMessage("Enter a valid 15-character GSTIN.");
        RuleFor(x => x.Invoice.SupplierName).MaximumLength(200);
        RuleFor(x => x.Invoice.SupplierAddress).MaximumLength(400);
        RuleFor(x => x.Invoice.SupplierState).MaximumLength(60);
        RuleFor(x => x.Invoice.Declaration).MaximumLength(500);
        RuleFor(x => x.Invoice.SupplierStateCode).Matches("^[0-9]{2}$").When(x => !string.IsNullOrWhiteSpace(x.Invoice.SupplierStateCode)).WithMessage("The state code is two digits, for example 27.");
    }
}

internal sealed class SettingsHandler(IOpsSettings settings, OpsAccess access)
{
    public async Task<Result<OpsSettingsDto>> GetAsync(CancellationToken cancellationToken) =>
        new OpsSettingsDto(
            await settings.GetAsync(OpsSettingKeys.Arrival, new ArrivalSetting(), cancellationToken),
            await settings.GetAsync(OpsSettingKeys.Numbering, new NumberingSetting(), cancellationToken),
            await settings.GetAsync(OpsSettingKeys.Pod, new PodSetting(), cancellationToken),
            await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken),
            await settings.GetAsync(OpsSettingKeys.Fleet, new FleetSetting(), cancellationToken));

    public async Task<Result<OpsSettingsDto>> SaveAsync(OpsSettingsDto request, CancellationToken cancellationToken)
    {
        if (access.Tenant() is { IsFailure: true } noTenant)
        {
            return noTenant.Error;
        }

        var tenant = access.TenantId!.Value;
        await settings.SaveAsync(OpsSettingKeys.Arrival, request.Arrival, tenant, cancellationToken);
        await settings.SaveAsync(OpsSettingKeys.Numbering, request.Numbering, tenant, cancellationToken);
        await settings.SaveAsync(OpsSettingKeys.Pod, request.Pod, tenant, cancellationToken);
        // The logo and signature are changed only through their own upload, so the form cannot point the settings at some other stored file.
        var current = await settings.GetAsync(OpsSettingKeys.Invoice, new InvoiceSetting(), cancellationToken);
        await settings.SaveAsync(OpsSettingKeys.Invoice, request.Invoice with { SeriesPrefix = request.Invoice.SeriesPrefix.Trim().ToUpperInvariant(), LogoKey = current.LogoKey, SignatureKey = current.SignatureKey }, tenant, cancellationToken);
        await settings.SaveAsync(OpsSettingKeys.Fleet, request.Fleet, tenant, cancellationToken);
        return await GetAsync(cancellationToken);
    }
}
