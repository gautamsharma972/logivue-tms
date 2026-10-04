using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using static Tms.UnitTests.Contracts.ContractTestData;

namespace Tms.UnitTests.Contracts;

public class PricingValidationTests
{
    [Fact]
    public void Weight_slabs_must_start_at_zero_and_join_without_gaps_or_overlaps()
    {
        new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(0, 100, 10), new WeightSlab(100, null, 8)], 0, 0).Validate().IsSuccess.ShouldBeTrue();

        new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(10, 100, 10)], 0, 0).Validate().Error.Description.ShouldContain("start at 0");
        new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(0, 100, 10), new WeightSlab(120, null, 8)], 0, 0).Validate().Error.Description.ShouldContain("no gaps or overlaps");
        new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(0, 100, 10), new WeightSlab(90, null, 8)], 0, 0).Validate().IsFailure.ShouldBeTrue();
        new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(0, null, 10), new WeightSlab(100, null, 8)], 0, 0).Validate().Error.Description.ShouldContain("only the last slab");
        new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(0, 100, 0)], 0, 0).Validate().IsFailure.ShouldBeTrue();
        new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(0, 0, 5)], 0, 0).Validate().IsFailure.ShouldBeTrue();
        new WeightSlabPricing(SlabMode.Whole, [], 0, 0).Validate().IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Simple_pricing_shapes_reject_nonsense()
    {
        new FlatTripPricing(0).Validate().IsFailure.ShouldBeTrue();
        new PerKmPricing(0, 0, 0).Validate().IsFailure.ShouldBeTrue();
        new PerKmPricing(10, -1, 0).Validate().IsFailure.ShouldBeTrue();
        new DedicatedPricing(0, 0, 0, 0, 0).Validate().IsFailure.ShouldBeTrue();
        new DedicatedPricing(50_000, 2000, 12, 0, 0).Validate().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Each_pricing_shape_belongs_to_one_contract_type()
    {
        new FlatTripPricing(1).SuitsContractType(ContractType.Ftl).ShouldBeTrue();
        new FlatTripPricing(1).SuitsContractType(ContractType.Ptl).ShouldBeFalse();
        Slabs().SuitsContractType(ContractType.Ptl).ShouldBeTrue();
        new DedicatedPricing(1, 0, 0, 0, 0).SuitsContractType(ContractType.Dedicated).ShouldBeTrue();
    }

    [Fact]
    public void Pricing_survives_a_json_round_trip_with_its_concrete_type()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        Pricing original = Slabs(SlabMode.Incremental, minCharge: 150m);

        var json = System.Text.Json.JsonSerializer.Serialize(original, options);
        var back = System.Text.Json.JsonSerializer.Deserialize<Pricing>(json, options);

        json.ShouldContain("\"kind\":\"weightSlabs\"");
        back.ShouldBeOfType<WeightSlabPricing>().Slabs.Count.ShouldBe(3);
        back.ShouldBeOfType<WeightSlabPricing>().MinCharge.ShouldBe(150m);
    }
}

public class ContractTests
{
    private static RateCardSpec FtlSpec(decimal amount = 1000m, Guid? vehicle = null) =>
        Spec(Place.OfState("Maharashtra"), Place.OfState("Gujarat"), new FlatTripPricing(amount), vehicle ?? Truck32);

    [Fact]
    public void A_new_contract_is_a_draft_with_normalised_header_data()
    {
        var contract = Draft();

        contract.Status.ShouldBe(ContractStatus.Draft);
        contract.Revision.ShouldBe(1);
        contract.Reference.ShouldBe("CN-00001");
    }

    [Theory]
    [InlineData("", 0, 365, 30)]        // no title
    [InlineData("T", 365, 0, 30)]       // ends before it starts
    [InlineData("T", 0, 4000, 30)]      // longer than 10 years
    [InlineData("T", 0, 365, 400)]      // payment terms out of range
    public void Header_validation_reports_the_problem(string title, int fromOffset, int toOffset, int paymentDays)
    {
        var from = Today.AddDays(fromOffset);

        Contract.Create(Tenant, "CN-1", Transporter, ContractType.Ftl, title, from, Today.AddDays(toOffset), paymentDays, null, null, Terms(), null)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_bad_fuel_clause_or_terms_block_the_header()
    {
        Contract.Create(Tenant, "CN-1", Transporter, ContractType.Ftl, "T", Today, Today.AddYears(1), 30, null, null, Terms(), Fuel(step: 0)).Error.Code.ShouldBe("contracts.fuel_invalid");
        Contract.Create(Tenant, "CN-1", Transporter, ContractType.Ftl, "T", Today, Today.AddYears(1), 30, null, null, Terms(volumetric: 5), null).Error.Code.ShouldBe("contracts.terms_invalid");
    }

    [Fact]
    public void Rates_are_validated_against_the_contract_type_row_by_row()
    {
        var ftl = Draft(ContractType.Ftl);
        var ptl = Draft(ContractType.Ptl);

        ftl.ReplaceRates([Spec(Place.OfState("Goa"), Place.OfState("Goa"), Slabs(), Truck32)]).Error.Description.ShouldContain("Rate 1: this pricing does not suit a Ftl contract");
        ftl.ReplaceRates([FtlSpec(), Spec(Place.OfState("Goa"), Place.OfState("Goa"), new FlatTripPricing(500m), vehicle: null)]).Error.Description.ShouldContain("Rate 2: choose a vehicle type");
        ptl.ReplaceRates([Spec(Place.OfState("Goa"), Place.OfState("Goa"), Slabs(), Truck32)]).Error.Description.ShouldContain("not tied to a vehicle type");
        ftl.ReplaceRates([Spec(Place.OfState("Goa"), Place.OfState("Goa"), new FlatTripPricing(0), Truck32)]).Error.Description.ShouldContain("Rate 1");
        ftl.ReplaceRates([Spec(Place.Anywhere, Place.Anywhere, new FlatTripPricing(10), Truck32)]).Error.Description.ShouldContain("needs a distance band");
        ftl.ReplaceRates([Spec(Place.OfState("Goa"), Place.OfState("Goa"), new FlatTripPricing(10), Truck32, minKm: 500, maxKm: 100)]).Error.Description.ShouldContain("distance band is invalid");
        ftl.RateCount.ShouldBe(0, "a rejected set changes nothing");
    }

    [Fact]
    public void Two_identical_lanes_are_rejected_but_different_vehicle_types_are_fine()
    {
        var contract = Draft();

        contract.ReplaceRates([FtlSpec(vehicle: Truck32), FtlSpec(vehicle: Truck14)]).IsSuccess.ShouldBeTrue();
        contract.ReplaceRates([FtlSpec(1000m), FtlSpec(2000m)]).Error.Description.ShouldContain("duplicates an earlier rate");
    }

    [Fact]
    public void Replacing_rates_counts_and_versions_them_for_the_audit_trail()
    {
        var contract = Draft();

        contract.ReplaceRates([FtlSpec(vehicle: Truck32), FtlSpec(vehicle: Truck14)]);
        contract.RateCount.ShouldBe(2);
        contract.RatesRevision.ShouldBe(1);

        contract.ReplaceRates([FtlSpec()]);
        contract.RateCount.ShouldBe(1);
        contract.RatesRevision.ShouldBe(2);
        contract.RateCards.ShouldHaveSingleItem();
    }

    [Fact]
    public void Submission_requires_rates_and_a_future_end_date()
    {
        var contract = Draft();
        contract.MissingForSubmission(Today).ShouldBe(["At least one rate"]);

        contract.ReplaceRates([FtlSpec()]);
        contract.MissingForSubmission(Today).ShouldBeEmpty();
        contract.MissingForSubmission(Today.AddYears(3)).ShouldBe(["The end date is already in the past"]);
    }

    [Fact]
    public void An_approved_contract_is_immutable()
    {
        var contract = Active(ContractType.Ftl, [FtlSpec()]);

        contract.Status.ShouldBe(ContractStatus.Active);
        contract.ReplaceRates([FtlSpec(2000m)]).Error.Code.ShouldBe("contracts.immutable");
        contract.UpdateHeader("New", Today, Today.AddYears(1), 30, null, null, Terms(), null).Error.Code.ShouldBe("contracts.immutable");
    }

    [Fact]
    public void A_contract_awaiting_approval_is_locked()
    {
        var contract = Draft();
        contract.ReplaceRates([FtlSpec()]);
        contract.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Pending, Now);

        contract.Status.ShouldBe(ContractStatus.PendingApproval);
        contract.ReplaceRates([FtlSpec(5m)]).Error.Code.ShouldBe("contracts.locked");
        contract.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Pending, Now).Error.Code.ShouldBe("contracts.not_submittable");
    }

    [Fact]
    public void Approval_outcomes_drive_the_status_and_stale_ones_are_ignored()
    {
        Contract Pending(out Guid id)
        {
            var c = Draft(); c.ReplaceRates([FtlSpec()]); id = Guid.NewGuid();
            c.MarkSubmitted(id, ApprovalStatus.Pending, Now);
            return c;
        }

        var approved = Pending(out var a);
        approved.ApplyApprovalOutcome(Guid.NewGuid(), ApprovalStatus.Approved, Now).ShouldBeFalse("someone else's request");
        approved.ApplyApprovalOutcome(a, ApprovalStatus.Approved, Now).ShouldBeTrue();
        approved.Status.ShouldBe(ContractStatus.Active);
        approved.ActivatedAt.ShouldBe(Now);

        var rejected = Pending(out var r);
        rejected.ApplyApprovalOutcome(r, ApprovalStatus.Rejected, Now);
        rejected.Status.ShouldBe(ContractStatus.Rejected);
        rejected.ReplaceRates([FtlSpec(9m)]).IsSuccess.ShouldBeTrue("a rejected contract can be corrected");

        var cancelled = Pending(out var c2);
        cancelled.ApplyApprovalOutcome(c2, ApprovalStatus.Cancelled, Now);
        cancelled.Status.ShouldBe(ContractStatus.Draft);
    }

    [Fact]
    public void An_instantly_approved_submission_activates_the_contract()
    {
        var contract = Draft();
        contract.ReplaceRates([FtlSpec()]);

        contract.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Approved, Now);

        contract.Status.ShouldBe(ContractStatus.Active);
    }

    [Fact]
    public void Termination_needs_a_reason_and_ends_the_contract_today()
    {
        var contract = Active(ContractType.Ftl, [FtlSpec()]);

        contract.Terminate(" ", Today).Error.Code.ShouldBe("contracts.reason_required");
        contract.Terminate("Service failures", Today).IsSuccess.ShouldBeTrue();

        contract.Status.ShouldBe(ContractStatus.Terminated);
        contract.EffectiveTo.ShouldBe(Today);
        contract.IsInForce(Today.AddDays(1)).ShouldBeFalse();
        Draft().Terminate("x", Today).Error.Code.ShouldBe("contracts.not_active");
    }

    [Fact]
    public void An_active_contract_past_its_end_date_reads_as_expired_before_the_job_marks_it()
    {
        var contract = Active(ContractType.Ftl, [FtlSpec()]);
        var after = contract.EffectiveTo.AddDays(1);

        contract.EffectiveStatus(Today).ShouldBe(ContractStatus.Active);
        contract.EffectiveStatus(after).ShouldBe(ContractStatus.Expired);
        contract.ExpireIfPast(Today).ShouldBeFalse();
        contract.ExpireIfPast(after).ShouldBeTrue();
        contract.Status.ShouldBe(ContractStatus.Expired);
    }

    [Fact]
    public void Days_until_expiry_counts_for_active_contracts_only()
    {
        var contract = Active(ContractType.Ftl, [FtlSpec()]);

        contract.DaysUntilExpiry(Today).ShouldBe(contract.EffectiveTo.DayNumber - Today.DayNumber);
        contract.DaysUntilExpiry(contract.EffectiveTo).ShouldBe(0);
        Draft().DaysUntilExpiry(Today).ShouldBeNull();
    }

    [Fact]
    public void A_revision_copies_everything_and_replaces_the_old_contract_on_the_day_it_starts()
    {
        var original = Active(ContractType.Ftl, [FtlSpec(vehicle: Truck32), FtlSpec(vehicle: Truck14)], Fuel(), Terms(loading: 200m));
        var starts = Today.AddDays(20);

        var revision = original.CreateRevision(starts, starts.AddYears(1), ownerUserId: null).Value;

        revision.Status.ShouldBe(ContractStatus.Draft);
        revision.Number.ShouldBe(original.Number);
        revision.Revision.ShouldBe(2);
        revision.Reference.ShouldBe("CN-00001 · R2");
        revision.RevisionOfId.ShouldBe(original.Id);
        revision.RateCount.ShouldBe(2);
        revision.RateCards.Select(c => c.Id).Intersect(original.RateCards.Select(c => c.Id)).ShouldBeEmpty("copies, not shared rows");
        revision.Terms.LoadingCharge.ShouldBe(200m);
        revision.Fuel.ShouldNotBeNull();

        revision.ReplaceRates([FtlSpec(vehicle: Truck32)]).IsSuccess.ShouldBeTrue("the revision is editable while the original is untouched");
        original.RateCount.ShouldBe(2);

        original.SupersedeBy(starts);
        original.Status.ShouldBe(ContractStatus.Superseded);
        original.EffectiveTo.ShouldBe(starts.AddDays(-1));
        original.IsInForce(starts.AddDays(-1)).ShouldBeTrue("still prices the shipments it governed");
        original.IsInForce(starts).ShouldBeFalse();
    }

    [Fact]
    public void Only_approved_contracts_can_be_revised()
    {
        Draft().CreateRevision(Today, Today.AddYears(1), null).Error.Code.ShouldBe("contracts.not_revisable");
    }
}
