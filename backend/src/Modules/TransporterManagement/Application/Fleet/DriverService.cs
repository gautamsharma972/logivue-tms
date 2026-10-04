using System.Text.RegularExpressions;
using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Fleet;

public sealed record DriverDto(long Id, long TransporterId, string FullName, string Mobile, string LicenceNumber, RecordStatus Status);

public sealed record SaveDriverRequest(string FullName, string Mobile, string LicenceNumber, RecordStatus? Status = null);

public interface IDriverService
{
    Task<IReadOnlyList<DriverDto>> ListAsync(long transporterId, CancellationToken cancellationToken = default);

    Task<DriverDto> CreateAsync(long transporterId, SaveDriverRequest request, CancellationToken cancellationToken = default);

    Task<DriverDto> UpdateAsync(long transporterId, long driverId, SaveDriverRequest request, CancellationToken cancellationToken = default);
}

public sealed class DriverService(
    IRepository<Transporter> transporters,
    IRepository<TransporterDriver> drivers,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<SaveDriverRequest> validator) : IDriverService
{
    public async Task<IReadOnlyList<DriverDto>> ListAsync(long transporterId, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        return (await drivers.ListAsync(d => d.TransporterId == transporterId, cancellationToken))
            .OrderBy(d => d.FullName).Select(ToDto).ToList();
    }

    public async Task<DriverDto> CreateAsync(long transporterId, SaveDriverRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var licence = Normalise(request.LicenceNumber);
        if (await drivers.AnyAsync(d => d.TransporterId == transporterId && d.LicenceNumber == licence, cancellationToken))
        {
            throw new ConflictException($"Licence number {licence} is already registered for this transporter.", "DRIVER_LICENCE_DUPLICATE");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var driver = new TransporterDriver
        {
            TransporterId = transporterId,
            FullName = request.FullName.Trim(),
            Mobile = request.Mobile.Trim(),
            LicenceNumber = licence,
            Status = RecordStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        drivers.Add(driver);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterDriver", driver.Id.ToString(), "DriverAdded",
            NewValueJson: AuditJson.Serialize(new { driver.FullName, driver.LicenceNumber })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(driver);
    }

    public async Task<DriverDto> UpdateAsync(long transporterId, long driverId, SaveDriverRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var driver = await drivers.FindAsync(driverId, cancellationToken);
        if (driver is null || driver.TransporterId != transporterId)
        {
            throw new NotFoundException($"Driver {driverId} was not found for transporter {transporterId}.", "DRIVER_NOT_FOUND");
        }

        var licence = Normalise(request.LicenceNumber);
        if (licence != driver.LicenceNumber
            && await drivers.AnyAsync(d => d.TransporterId == transporterId && d.LicenceNumber == licence, cancellationToken))
        {
            throw new ConflictException($"Licence number {licence} is already registered for this transporter.", "DRIVER_LICENCE_DUPLICATE");
        }

        var before = AuditJson.Serialize(driver);
        driver.FullName = request.FullName.Trim();
        driver.Mobile = request.Mobile.Trim();
        driver.LicenceNumber = licence;
        driver.Status = request.Status ?? driver.Status;
        driver.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterDriver", driverId.ToString(), "DriverUpdated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(driver)), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(driver);
    }

    /// <summary>Licence numbers are stored upper-case with no spaces or hyphens, so duplicates are caught however they are typed.</summary>
    public static string Normalise(string licence) => Regex.Replace(licence, "[\\s-]+", string.Empty).ToUpperInvariant();

    /// <summary>Mobile numbers are compared on their digits, so "+91 98200-55555" and "919820055555" match.</summary>
    public static string MobileDigits(string mobile) => new(mobile.Where(char.IsDigit).ToArray());

    private static DriverDto ToDto(TransporterDriver d) =>
        new(d.Id, d.TransporterId, d.FullName, d.Mobile, d.LicenceNumber, d.Status);
}

public sealed class SaveDriverRequestValidator : AbstractValidator<SaveDriverRequest>
{
    public SaveDriverRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Mobile).NotEmpty().Must(m => DriverDigits(m).Length is >= 10 and <= 15)
            .WithMessage("Enter a valid mobile number.");
        RuleFor(x => x.LicenceNumber).NotEmpty()
            .Must(l => Regex.IsMatch(DriverService.Normalise(l), "^[A-Z0-9]{5,40}$"))
            .WithMessage("Use 5-40 letters or digits for the licence number.");
    }

    private static string DriverDigits(string mobile) => DriverService.MobileDigits(mobile);
}
