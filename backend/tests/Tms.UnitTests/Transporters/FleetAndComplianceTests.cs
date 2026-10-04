using Tms.Modules.Transporters.Domain;

namespace Tms.UnitTests.Transporters;

public class FleetAndComplianceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Transporter = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 6, 1);

    private static ComplianceDocument Doc(DocumentKind kind, DateOnly? expires, OwnerKind owner = OwnerKind.Vehicle, DateTimeOffset? supersededAt = null)
    {
        var doc = ComplianceDocument.Create(Tenant, Transporter, owner, Guid.NewGuid(), kind, "N1", null, expires, "k", "f.pdf", "application/pdf", 10).Value;
        if (supersededAt is { } at)
        {
            doc.Supersede(at);
        }

        return doc;
    }

    private static IEnumerable<ComplianceDocument> FullSet(DateOnly insurance, DateOnly fitness) =>
    [
        Doc(DocumentKind.RegistrationCertificate, null),
        Doc(DocumentKind.Insurance, insurance),
        Doc(DocumentKind.Fitness, fitness),
        Doc(DocumentKind.Permit, Today.AddYears(1)),
    ];

    [Fact]
    public void Vehicle_Registration_IsNormalisedAndValidated()
    {
        var vehicle = Vehicle.Create(Tenant, Transporter, "mh 12 ab 1234", Guid.NewGuid(), VehicleOwnership.Owned, " Tata ", 2021).Value;

        vehicle.RegistrationNumber.ShouldBe("MH12AB1234");
        vehicle.Make.ShouldBe("Tata");
        Vehicle.Create(Tenant, Transporter, "NOT-A-PLATE", Guid.NewGuid(), VehicleOwnership.Owned, null, null).Error.ValidationErrors!.ShouldContainKey("registrationNumber");
        Vehicle.Create(Tenant, Transporter, "MH12AB1234", Guid.Empty, VehicleOwnership.Owned, null, 1950).Error.ValidationErrors!.Keys
            .ShouldBe(["vehicleTypeId", "yearOfManufacture"], ignoreOrder: true);
    }

    [Fact]
    public void Driver_PhoneAndLicence_AreValidated()
    {
        var driver = Driver.Create(Tenant, Transporter, " Ramesh Yadav ", "+91 98765 43210", "mh12 2019 0001234").Value;

        driver.Phone.ShouldBe("9876543210");
        driver.LicenseNumber.ShouldBe("MH1220190001234");
        Driver.Create(Tenant, Transporter, "X", "1", null).Error.ValidationErrors!.ShouldContainKey("phone");
        Driver.Create(Tenant, Transporter, "X", "9876543210", "AB1").Error.ValidationErrors!.ShouldContainKey("licenseNumber");
    }

    [Fact]
    public void VehicleType_Defaults_AreSane_AndCodesValidated()
    {
        VehicleType.Defaults.Select(d => d.Code).ShouldBeUnique();
        VehicleType.Defaults.ShouldAllBe(d => d.PayloadKg > 0);
        VehicleType.Create(Tenant, "bad code!", "Odd", 1000, null).IsFailure.ShouldBeTrue();
        VehicleType.Create(Tenant, "custom_8t", "Custom 8 T", 8000, 40m).Value.Code.ShouldBe("CUSTOM_8T");
        VehicleType.Create(Tenant, "x", "X", 0, null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Document_KindMustSuitTheOwner_AndExpiryIsRequiredWhereItMatters()
    {
        ComplianceDocument.Create(Tenant, Transporter, OwnerKind.Driver, Guid.NewGuid(), DocumentKind.Insurance, null, null, Today, "k", "f", "application/pdf", 1)
            .Error.ValidationErrors!.ShouldContainKey("kind");
        ComplianceDocument.Create(Tenant, Transporter, OwnerKind.Vehicle, Guid.NewGuid(), DocumentKind.Insurance, null, null, null, "k", "f", "application/pdf", 1)
            .Error.ValidationErrors!.ShouldContainKey("expiresOn");
        ComplianceDocument.Create(Tenant, Transporter, OwnerKind.Vehicle, Guid.NewGuid(), DocumentKind.Fitness, null, Today, Today.AddDays(-1), "k", "f", "application/pdf", 1)
            .Error.ValidationErrors!.ShouldContainKey("expiresOn");
        ComplianceDocument.Create(Tenant, Transporter, OwnerKind.Transporter, Guid.NewGuid(), DocumentKind.PanCard, null, null, null, "k", "f", "application/pdf", 1)
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ExpiryStatus_IsInclusiveOfTheLastDay()
    {
        Doc(DocumentKind.PanCard, null, OwnerKind.Transporter).StatusOn(Today).ShouldBe(ExpiryStatus.NoExpiry);
        Doc(DocumentKind.Insurance, Today.AddDays(-1)).StatusOn(Today).ShouldBe(ExpiryStatus.Expired);
        Doc(DocumentKind.Insurance, Today).StatusOn(Today).ShouldBe(ExpiryStatus.ExpiringSoon);
        Doc(DocumentKind.Insurance, Today.AddDays(30)).StatusOn(Today).ShouldBe(ExpiryStatus.ExpiringSoon);
        Doc(DocumentKind.Insurance, Today.AddDays(31)).StatusOn(Today).ShouldBe(ExpiryStatus.Valid);
    }

    [Fact]
    public void Vehicle_WithAllPapersInDate_IsCompliant()
    {
        var result = ComplianceEvaluator.ForVehicle(FullSet(Today.AddMonths(6), Today.AddMonths(8)), Today);

        result.Status.ShouldBe(ComplianceStatus.Compliant);
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void Vehicle_WithAnExpiringPaper_IsFlaggedButUsable()
    {
        var result = ComplianceEvaluator.ForVehicle(FullSet(Today.AddDays(10), Today.AddMonths(8)), Today);

        result.Status.ShouldBe(ComplianceStatus.ExpiringSoon);
        result.Issues.ShouldHaveSingleItem().ShouldStartWith("Insurance expires on");
    }

    [Fact]
    public void Vehicle_WithAnExpiredOrMissingPaper_IsNonCompliant()
    {
        var expired = ComplianceEvaluator.ForVehicle(FullSet(Today.AddDays(-5), Today.AddMonths(8)), Today);
        expired.Status.ShouldBe(ComplianceStatus.NonCompliant);
        expired.Issues.ShouldHaveSingleItem().ShouldStartWith("Insurance expired on");

        var missing = ComplianceEvaluator.ForVehicle([Doc(DocumentKind.RegistrationCertificate, null)], Today);
        missing.Status.ShouldBe(ComplianceStatus.NonCompliant);
        missing.Issues.ShouldBe(["Insurance missing", "Fitness certificate missing", "Permit missing"]);
    }

    [Fact]
    public void ReplacingAPaper_UsesTheNewOneAndIgnoresTheSupersededOne()
    {
        var superseded = Doc(DocumentKind.Insurance, Today.AddDays(-100), supersededAt: DateTimeOffset.UtcNow);
        var current = FullSet(Today.AddMonths(6), Today.AddMonths(8)).Where(d => d.Kind != DocumentKind.Insurance).Append(Doc(DocumentKind.Insurance, Today.AddMonths(11)));

        ComplianceEvaluator.ForVehicle(current.Append(superseded), Today).Status.ShouldBe(ComplianceStatus.Compliant);
    }

    [Fact]
    public void Driver_NeedsAValidLicence()
    {
        ComplianceEvaluator.ForDriver([], Today).Status.ShouldBe(ComplianceStatus.NonCompliant);
        ComplianceEvaluator.ForDriver([Doc(DocumentKind.DrivingLicense, Today.AddYears(2), OwnerKind.Driver)], Today).Status.ShouldBe(ComplianceStatus.Compliant);
        ComplianceEvaluator.ForDriver([Doc(DocumentKind.DrivingLicense, Today.AddDays(-1), OwnerKind.Driver)], Today).Status.ShouldBe(ComplianceStatus.NonCompliant);
    }
}
