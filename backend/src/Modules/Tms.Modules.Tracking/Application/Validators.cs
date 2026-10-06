using FluentValidation;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Application.Mobile;

namespace Tms.Modules.Tracking.Application;

internal sealed class StartTrackingRequestValidator : AbstractValidator<StartTrackingRequest>
{
    public StartTrackingRequestValidator()
    {
        RuleFor(x => x.TripReference).NotEmpty().MaximumLength(40);
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.VehicleReference).MaximumLength(20);
        RuleFor(x => x.DriverReference).MaximumLength(150);
        RuleFor(x => x.ClientKey).MaximumLength(100);
    }
}

internal sealed class StopTrackingRequestValidator : AbstractValidator<StopTrackingRequest>
{
    public StopTrackingRequestValidator()
    {
        RuleFor(x => x.TripReference).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Reason).MaximumLength(200);
        RuleFor(x => x.ClientKey).MaximumLength(100);
    }
}

internal sealed class LocationBatchValidator : AbstractValidator<LocationBatch>
{
    public LocationBatchValidator()
    {
        RuleFor(x => x.TripReference).NotEmpty().MaximumLength(40);
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Locations).NotEmpty().Must(l => l.Count <= 1000).WithMessage("Send at most 1000 locations at a time.");
        RuleForEach(x => x.Locations).ChildRules(l => l.RuleFor(p => p.ClientLocationId).NotEmpty().MaximumLength(64));
        RuleFor(x => x.BatteryPercentage).InclusiveBetween(0, 100).When(x => x.BatteryPercentage.HasValue);
    }
}

internal sealed class SingleLocationRequestValidator : AbstractValidator<SingleLocationRequest>
{
    public SingleLocationRequestValidator()
    {
        RuleFor(x => x.TripReference).NotEmpty().MaximumLength(40);
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Location).NotNull();
        RuleFor(x => x.Location.ClientLocationId).NotEmpty().MaximumLength(64).When(x => x.Location is not null);
    }
}

internal sealed class DeviceStatusRequestValidator : AbstractValidator<DeviceStatusRequest>
{
    public DeviceStatusRequestValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationPermission).Must(p => p is null or "Granted" or "Denied" or "Unknown").WithMessage("Permission is Granted, Denied or Unknown.");
    }
}

internal sealed class SaveGeofenceRequestValidator : AbstractValidator<SaveGeofenceRequest>
{
    public SaveGeofenceRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Type).IsInEnum();
    }
}

internal sealed class OverrideEtaRequestValidator : AbstractValidator<OverrideEtaRequest>
{
    public OverrideEtaRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class ManualMilestoneRequestValidator : AbstractValidator<ManualMilestoneRequest>
{
    public ManualMilestoneRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class NoteRequestValidator : AbstractValidator<NoteRequest>
{
    public NoteRequestValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
}

internal sealed class EscalateExceptionRequestValidator : AbstractValidator<EscalateExceptionRequest>
{
    public EscalateExceptionRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Level).MaximumLength(100);
    }
}

internal sealed class ResolveExceptionRequestValidator : AbstractValidator<ResolveExceptionRequest>
{
    public ResolveExceptionRequestValidator()
    {
        RuleFor(x => x.RootCause).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ActionTaken).MaximumLength(500);
    }
}

internal sealed class CreateLinkRequestValidator : AbstractValidator<CreateLinkRequest>
{
    public CreateLinkRequestValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.CustomerReference).MaximumLength(64);
        RuleFor(x => x.CustomerName).MaximumLength(200);
    }
}
