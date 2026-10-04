using Tms.Modules.Transporters.Domain;

namespace Tms.UnitTests.Transporters;

public class MasterDataTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ComplianceDocument Doc(DocumentKind kind, DateOnly? expires, OwnerKind owner = OwnerKind.Vehicle) =>
        ComplianceDocument.Create(Tenant, Guid.NewGuid(), owner, Guid.NewGuid(), kind, null, null, expires, "k", "f.pdf", "application/pdf", 10).Value;

    private static List<ComplianceDocument> AllVehiclePapers(DateOnly expires) =>
    [
        Doc(DocumentKind.RegistrationCertificate, null), Doc(DocumentKind.Insurance, expires), Doc(DocumentKind.Fitness, expires), Doc(DocumentKind.Permit, expires),
    ];

    [Fact]
    public void Without_any_rules_the_built_in_defaults_behave_as_before()
    {
        var today = new DateOnly(2026, 10, 5);
        ComplianceEvaluator.ForVehicle(AllVehiclePapers(today.AddDays(90)), today).Status.ShouldBe(ComplianceStatus.Compliant);
        ComplianceEvaluator.ForVehicle(AllVehiclePapers(today.AddDays(10)), today).Status.ShouldBe(ComplianceStatus.ExpiringSoon);
        ComplianceEvaluator.ForVehicle(AllVehiclePapers(today.AddDays(-1)), today).Status.ShouldBe(ComplianceStatus.NonCompliant);
        ComplianceEvaluator.ForVehicle([], today).Issues.Count.ShouldBe(4);
        ComplianceEvaluator.ForDriver([], today).Issues.ShouldBe(["Driving licence missing"]);
    }

    [Fact]
    public void A_longer_reminder_flags_a_paper_earlier_and_a_non_blocking_lapse_is_only_a_warning()
    {
        var today = new DateOnly(2026, 10, 5);
        var insurance = DocumentRule.Create(Tenant, DocumentKind.Insurance, new DocumentRuleValues(true, true, 60, false, true)).Value;
        var policy = new DocumentPolicy([insurance]);

        ComplianceEvaluator.ForVehicle(AllVehiclePapers(today.AddDays(45)), today, policy).Status.ShouldBe(ComplianceStatus.ExpiringSoon);

        var lapsed = AllVehiclePapers(today.AddDays(90));
        lapsed[1] = Doc(DocumentKind.Insurance, today.AddDays(-3));
        var result = ComplianceEvaluator.ForVehicle(lapsed, today, policy);
        result.Status.ShouldBe(ComplianceStatus.ExpiringSoon); // expired insurance no longer stops the vehicle
        result.Issues.ShouldContain(i => i.StartsWith("Insurance expired", StringComparison.Ordinal));
    }

    [Fact]
    public void A_paper_the_tenant_requires_or_switches_off_changes_what_counts_as_missing()
    {
        var today = new DateOnly(2026, 10, 5);
        var puc = DocumentRule.Create(Tenant, DocumentKind.Puc, new DocumentRuleValues(true, true, 15, true, true)).Value;
        ComplianceEvaluator.ForVehicle(AllVehiclePapers(today.AddDays(90)), today, new DocumentPolicy([puc])).Issues.ShouldBe(["PUC certificate missing"]);

        var noPermit = DocumentRule.Create(Tenant, DocumentKind.Permit, new DocumentRuleValues(false, true, 30, true, false)).Value;
        var papers = AllVehiclePapers(today.AddDays(90));
        papers.RemoveAt(3);
        ComplianceEvaluator.ForVehicle(papers, today, new DocumentPolicy([noPermit])).Status.ShouldBe(ComplianceStatus.Compliant);
    }

    [Fact]
    public void Whether_a_date_is_required_follows_the_rule()
    {
        ComplianceDocument.Create(Tenant, Guid.NewGuid(), OwnerKind.Vehicle, Guid.NewGuid(), DocumentKind.Insurance, null, null, null, "k", "f.pdf", "application/pdf", 10).Error.ValidationErrors!.ShouldContainKey("expiresOn");

        var relaxed = new DocumentPolicy([DocumentRule.Create(Tenant, DocumentKind.Insurance, new DocumentRuleValues(true, false, 30, true, true)).Value]);
        ComplianceDocument.Create(Tenant, Guid.NewGuid(), OwnerKind.Vehicle, Guid.NewGuid(), DocumentKind.Insurance, null, null, null, "k", "f.pdf", "application/pdf", 10, relaxed).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_rule_must_be_sensible()
    {
        DocumentRule.Create(Tenant, DocumentKind.Puc, new DocumentRuleValues(false, true, -1, true, true)).Error.ValidationErrors!.ShouldContainKey("renewalReminderDays");
        DocumentRule.Create(Tenant, DocumentKind.Puc, new DocumentRuleValues(false, true, 400, true, true)).IsFailure.ShouldBeTrue();
        DocumentRule.Create(Tenant, DocumentKind.Puc, new DocumentRuleValues(true, true, 30, true, false)).IsFailure.ShouldBeTrue(); // switched off yet mandatory
    }

    [Fact]
    public void Master_lists_merge_the_built_ins_with_the_tenants_own_entries_and_overrides()
    {
        var own = MasterItem.Create(Tenant, MasterKind.Capability, "cold chain", "Cold chain").Value;
        own.Code.ShouldBe("COLD_CHAIN");

        var switchedOff = MasterItem.Create(Tenant, MasterKind.Capability, CapabilityCatalog.FoodGrade, "Food grade").Value;
        switchedOff.Set("Food grade", false);

        var merged = MasterCatalog.Merge(MasterKind.Capability, [own, switchedOff]);
        merged.ShouldContain(e => e.Code == "COLD_CHAIN" && e.IsActive && !e.IsBuiltIn);
        merged.Single(e => e.Code == CapabilityCatalog.FoodGrade).IsActive.ShouldBeFalse();
        merged.Single(e => e.Code == CapabilityCatalog.Hazardous).IsBuiltIn.ShouldBeTrue();

        MasterCatalog.IsActive(MasterKind.Capability, [own, switchedOff], "cold chain").ShouldBeTrue();
        MasterCatalog.IsActive(MasterKind.Capability, [own, switchedOff], CapabilityCatalog.FoodGrade).ShouldBeFalse();
        MasterCatalog.IsActive(MasterKind.TransporterType, [own], "3PL").ShouldBeTrue();
        MasterCatalog.IsActive(MasterKind.TransporterType, [own], "TELEPORTER").ShouldBeFalse();
        MasterItem.Create(Tenant, MasterKind.TransporterType, "bad code!", "x").Error.ValidationErrors!.ShouldContainKey("code");
    }
}
