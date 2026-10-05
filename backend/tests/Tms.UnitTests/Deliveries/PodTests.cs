using Tms.Modules.Deliveries.Domain;
using static Tms.UnitTests.Deliveries.DeliveryTestData;

namespace Tms.UnitTests.Deliveries;

public class PodTests
{
    private static Delivery Delivered(Action<Delivery>? shape = null)
    {
        var d = Arrived();
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100)).IsSuccess.ShouldBeTrue();
        shape?.Invoke(d);
        return d;
    }

    private static PodValidationInput Input(PodRecord pod, Delivery d, PodRulesSetting? rules = null, IReadOnlySet<string>? duplicates = null) =>
        new(pod, d, rules ?? Rules, Strict, Discrepancy, PhotoDamage, duplicates ?? new HashSet<string>(), Now);

    private static IReadOnlyList<PodCheck> Check(PodRecord pod, Delivery d, PodRulesSetting? rules = null, IReadOnlySet<string>? duplicates = null) =>
        PodValidator.Validate(Input(pod, d, rules, duplicates));

    [Fact]
    public void A_new_proof_copies_the_delivery_quantities_and_starts_as_a_draft()
    {
        var d = Arrived(100, ("SKU-002", 50));
        DeliveryTestData.Complete(d, DeliveryOutcome.Full, Qty(d, 100), Qty(d, 50, index: 1));
        var pod = PodFor(d);

        pod.Status.ShouldBe(PodStatus.Draft);
        pod.Items.Select(i => (i.SkuReference, i.DeliveredQuantity)).ShouldBe([("SKU-001", 100m), ("SKU-002", 50m)]);
        pod.PodVersion.ShouldBe(1);
        pod.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void The_rules_say_what_a_proof_still_lacks_and_it_becomes_captured_when_nothing_is_missing()
    {
        var d = Delivered();
        var pod = PodFor(d, recipient: null);
        var before = pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now);
        before.Missing.ShouldContain(m => m.Contains("name of the person"));
        before.Missing.ShouldContain(m => m.Contains("photo"));

        pod.SetProof(ProofMethod.Photo, "Anil Kumar", null, null, null, false);
        AddPhoto(pod);
        var after = pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now);
        after.IsMet.ShouldBeTrue();
        pod.Refresh(after);
        pod.Status.ShouldBe(PodStatus.Captured);
    }

    [Fact]
    public void A_signature_otp_or_contactless_method_needs_its_own_evidence()
    {
        var d = Delivered();
        var pod = PodFor(d, ProofMethod.Signature);
        AddPhoto(pod);
        pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).Missing.ShouldContain(m => m.Contains("signature"));
        pod.AddSignature("Anil Kumar", null, "k/sig.png", "sig", Now, Here).IsSuccess.ShouldBeTrue();
        pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).IsMet.ShouldBeTrue();

        var otp = PodFor(d, ProofMethod.Otp);
        AddPhoto(otp);
        otp.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).Missing.ShouldContain(m => m.Contains("one-time code"));

        var contactless = PodFor(d, ProofMethod.Contactless, recipient: "Security guard");
        AddPhoto(contactless);
        contactless.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).Missing.ShouldContain(m => m.Contains("contactless"));
        contactless.SetProof(ProofMethod.Contactless, "Security guard", null, null, null, driverConfirmed: true);
        contactless.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).IsMet.ShouldBeTrue();
    }

    [Fact]
    public void A_tenant_that_requires_a_signature_gets_one_whatever_method_was_chosen()
    {
        var d = Delivered();
        var pod = PodFor(d);
        AddPhoto(pod);
        var strict = Rules with { SignatureRequired = true };
        pod.CheckRequirements(strict, Discrepancy, PhotoDamage, Now).Missing.ShouldContain(m => m.Contains("signature"));
    }

    [Fact]
    public void Damage_needs_a_photo_of_the_damage_and_a_required_acknowledgement()
    {
        var d = Arrived();
        DeliveryTestData.Complete(d, DeliveryOutcome.Damaged, Qty(d, 98, damaged: 2));
        var pod = PodFor(d);
        AddPhoto(pod);
        pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).Missing.ShouldContain("A photo of the damage");

        AddPhoto(pod, "h2", EvidenceType.DamagePhoto);
        pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).IsMet.ShouldBeTrue();

        var ack = Discrepancy with { DamageAcknowledgementRequired = true };
        pod.CheckRequirements(Rules, ack, PhotoDamage, Now).Missing.ShouldContain(m => m.Contains("acknowledgement"));
        pod.AcknowledgeByCustomer();
        pod.CheckRequirements(Rules, ack, PhotoDamage, Now).IsMet.ShouldBeTrue();
    }

    [Fact]
    public void An_incomplete_proof_cannot_be_submitted_and_the_message_lists_what_is_missing()
    {
        var d = Delivered();
        var pod = PodFor(d);
        var result = pod.Submit(pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now), Now);

        result.Error.Code.ShouldBe("pods.incomplete");
        result.Error.ValidationErrors!["pod"].ShouldContain(m => m.Contains("photo"));
        pod.Status.ShouldBe(PodStatus.Draft);
    }

    [Fact]
    public void A_removed_photo_stays_on_record_but_no_longer_counts()
    {
        var d = Delivered();
        var pod = PodFor(d);
        var photo = AddPhoto(pod);

        pod.RemoveEvidence(photo.Id, " ", Now).Error.Code.ShouldBe("pods.reason_required");
        pod.RemoveEvidence(photo.Id, "Blurred", Now).IsSuccess.ShouldBeTrue();

        pod.Evidence.Count.ShouldBe(1);
        photo.IsActive.ShouldBeFalse();
        photo.RemovedReason.ShouldBe("Blurred");
        pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now).Missing.ShouldContain(m => m.Contains("photo"));
    }

    [Fact]
    public void A_complete_proof_passes_every_check()
    {
        var d = Delivered();
        var pod = PodFor(d);
        AddPhoto(pod);

        var checks = Check(pod, d);

        checks.ShouldAllBe(c => c.Status == ValidationOutcome.Valid);
        checks.Select(c => c.Type).Distinct().ShouldBe(["Structural", "Quantity", "Evidence", "Business"], ignoreOrder: true);
    }

    [Fact]
    public void A_check_that_does_not_apply_is_not_a_failure()
    {
        var d = Delivered();
        var pod = PodFor(d);
        AddPhoto(pod);
        var relaxed = Rules with { GpsRequired = false, PhotoRequired = false };
        var noFix = PodRecord.Create(d, "POD-2", GeoFix.None, GeofenceStatus.NotApplicable, null, Now);
        noFix.SetProof(ProofMethod.Photo, "Anil", null, null, null, false);

        var checks = Check(noFix, d, relaxed);

        checks.Single(c => c.Check == "Gps").Status.ShouldBe(ValidationOutcome.Valid);
        checks.Single(c => c.Check == "Gps").Message.ShouldBe("Not applicable.");
        checks.Single(c => c.Check == "Geofence").Message.ShouldBe("Not applicable.");
        checks.ShouldNotContain(c => c.Status == ValidationOutcome.Invalid);
    }

    [Fact]
    public void Missing_required_evidence_is_invalid()
    {
        var d = Delivered();
        var pod = PodFor(d, ProofMethod.Signature);
        var checks = Check(pod, d);

        checks.Single(c => c.Check == "Signature").Status.ShouldBe(ValidationOutcome.Invalid);
        checks.Single(c => c.Check == "Photos").Status.ShouldBe(ValidationOutcome.Invalid);
    }

    [Fact]
    public void A_shortage_or_damage_sends_the_proof_to_a_reviewer_not_to_rejection()
    {
        var d = Arrived();
        DeliveryTestData.Complete(d, DeliveryOutcome.Shortage, Qty(d, 97, @short: 3));
        var pod = PodFor(d);
        AddPhoto(pod);

        var checks = Check(pod, d);

        checks.Single(c => c.Check == "Discrepancy").Status.ShouldBe(ValidationOutcome.RequiresReview);
        checks.ShouldNotContain(c => c.Status == ValidationOutcome.Invalid);
    }

    [Fact]
    public void Quantities_on_the_proof_that_do_not_reconcile_need_review_and_impossible_ones_are_invalid()
    {
        var d = Arrived();
        DeliveryTestData.Complete(d, DeliveryOutcome.Shortage, Qty(d, 90, @short: 3));
        var pod = PodFor(d);
        AddPhoto(pod);
        var mismatch = Check(pod, d).Single(c => c.Type == "Quantity");
        mismatch.Status.ShouldBe(ValidationOutcome.RequiresReview);
        mismatch.Message.ShouldContain("difference +7");

        var over = Delivered();
        var overPod = PodFor(over);
        AddPhoto(overPod);
        over.Items[0].Record(new ItemQuantities(over.Items[0].Id, 120, 0, 0, 0)); // the delivery was changed behind the proof's back
        Check(overPod, over).Single(c => c.Type == "Quantity").Status.ShouldBe(ValidationOutcome.Invalid);
    }

    [Fact]
    public void A_delivery_outside_its_geofence_is_flagged_for_review_with_the_distance()
    {
        var d = Delivered();
        var pod = PodRecord.Create(d, "POD-3", Here, GeofenceStatus.Outside, null, Now);
        pod.SetProof(ProofMethod.Photo, "Anil", null, null, null, false);
        AddPhoto(pod);

        Check(pod, d).Single(c => c.Check == "Geofence").Status.ShouldBe(ValidationOutcome.RequiresReview);

        var vague = PodRecord.Create(d, "POD-4", Here, GeofenceStatus.AccuracyInsufficient, null, Now);
        vague.SetProof(ProofMethod.Photo, "Anil", null, null, null, false);
        AddPhoto(vague);
        Check(vague, d).Single(c => c.Check == "Geofence").Status.ShouldBe(ValidationOutcome.Warning);
        Check(vague, d, Rules with { GeofenceRequired = true }).Single(c => c.Check == "Geofence").Status.ShouldBe(ValidationOutcome.RequiresReview);
    }

    [Fact]
    public void A_file_already_on_another_proof_is_a_potential_duplicate_for_review()
    {
        var d = Delivered();
        var pod = PodFor(d);
        AddPhoto(pod, "seen-before");

        var checks = Check(pod, d, duplicates: new HashSet<string> { "seen-before" });

        checks.Single(c => c.Check == "Duplicate").Status.ShouldBe(ValidationOutcome.RequiresReview);
    }

    [Fact]
    public void Lateness_is_a_warning_not_a_failure()
    {
        var d = Arrived();
        d.Complete(DeliveryOutcome.Full, [Qty(d, 100)], null, Now.AddHours(10), null, "Ramesh", Here, Strict, Driver, Now.AddHours(10));
        var pod = PodFor(d);
        AddPhoto(pod);

        Check(pod, d).Single(c => c.Check == "Timing").Status.ShouldBe(ValidationOutcome.Warning);
    }

    private static PodRecord Submitted(Delivery d)
    {
        var pod = PodFor(d);
        AddPhoto(pod);
        pod.Submit(pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now), Now).IsSuccess.ShouldBeTrue();
        return pod;
    }

    [Fact]
    public void A_submitted_proof_that_passes_is_accepted_and_keeps_its_history()
    {
        var d = Delivered();
        var pod = Submitted(d);
        pod.ApplyValidation(Check(pod, d).Select(c => (c.Type, c.Check, c.Status, c.Message)), Now);

        pod.Accept(Guid.NewGuid(), false, Now.AddHours(1), "Looks right").IsSuccess.ShouldBeTrue();

        pod.Status.ShouldBe(PodStatus.Accepted);
        pod.ApprovedAt.ShouldBe(Now.AddHours(1));
        pod.Reviews.Select(r => r.Action).ShouldBe(["Accepted"]);
        pod.IsEditable.ShouldBeFalse();
        pod.AddEvidence(EvidenceType.PackagePhoto, "k", "f.jpg", "image/jpeg", 1, "h", Now, Here, null, null, null, null, null, null).Error.Code.ShouldBe("pods.locked");
    }

    [Fact]
    public void A_proof_that_fails_a_mandatory_check_cannot_be_accepted()
    {
        var d = Delivered();
        var pod = Submitted(d);
        pod.ApplyValidation([("Evidence", "Signature", ValidationOutcome.Invalid, "A signature is required and was not captured.")], Now);

        var result = pod.Accept(null, false, Now);

        result.Error.Code.ShouldBe("pods.validation_failed");
        pod.Status.ShouldBe(PodStatus.Submitted);
    }

    [Fact]
    public void A_rejection_needs_a_reason_and_the_transporter_can_resubmit()
    {
        var d = Delivered();
        var pod = Submitted(d);

        pod.Reject(" ", null, Now).Error.Code.ShouldBe("pods.reason_required");
        pod.Reject("Photo is of the wrong site", null, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        pod.Status.ShouldBe(PodStatus.Rejected);
        pod.RejectionCount.ShouldBe(1);
        pod.RejectionReason.ShouldBe("Photo is of the wrong site");

        AddPhoto(pod, "better");
        pod.Submit(pod.CheckRequirements(Rules, Discrepancy, PhotoDamage, Now), Now.AddHours(2)).IsSuccess.ShouldBeTrue();
        pod.Status.ShouldBe(PodStatus.Submitted);
        pod.FirstSubmittedAt.ShouldBe(Now);
        pod.SubmittedAt.ShouldBe(Now.AddHours(2));
        pod.Reviews.Select(r => r.Action).ShouldBe(["Rejected", "Resubmitted"]);
    }

    [Fact]
    public void Asking_for_more_evidence_is_a_softer_return_than_a_rejection()
    {
        var d = Delivered();
        var pod = Submitted(d);
        pod.RequestResubmission("Add a photo of the unloading bay", null, Now).IsSuccess.ShouldBeTrue();
        pod.Status.ShouldBe(PodStatus.ResubmissionRequired);
        pod.IsEditable.ShouldBeTrue();
    }

    [Fact]
    public void A_proof_that_is_not_waiting_for_review_cannot_be_accepted_or_rejected()
    {
        var d = Delivered();
        var pod = PodFor(d);
        pod.Accept(null, false, Now).Error.Code.ShouldBe("pods.not_reviewable");
        pod.Reject("x", null, Now).Error.Code.ShouldBe("pods.not_reviewable");
    }

    [Fact]
    public void Correcting_an_accepted_proof_starts_a_new_version_and_leaves_the_accepted_one_untouched()
    {
        var d = Delivered();
        var pod = Submitted(d);
        pod.Accept(null, true, Now).IsSuccess.ShouldBeTrue();

        pod.StartCorrection(d, " ", null, Now).Error.Code.ShouldBe("pods.reason_required");
        var next = pod.StartCorrection(d, "Recipient name was misspelt", null, Now.AddDays(1)).Value;

        pod.Status.ShouldBe(PodStatus.Accepted);
        pod.IsCurrent.ShouldBeFalse();
        pod.RecipientName.ShouldBe("Anil Kumar");
        next.PodVersion.ShouldBe(2);
        next.PodNumber.ShouldBe(pod.PodNumber);
        next.SupersedesPodId.ShouldBe(pod.Id);
        next.Status.ShouldBe(PodStatus.Draft);
        next.IsCurrent.ShouldBeTrue();
        next.Evidence.Count.ShouldBe(pod.Evidence.Count); // copied, the originals untouched
        next.Evidence[0].FileKey.ShouldBe(pod.Evidence[0].FileKey);
        pod.Reviews.Select(r => r.Action).ShouldContain("CorrectionRequested");
        next.StartCorrection(d, "again", null, Now).Error.Code.ShouldBe("pods.not_correctable"); // only an accepted, current proof
    }

    [Fact]
    public void A_reviewers_edit_of_a_value_is_audited_with_old_new_and_reason()
    {
        var d = Delivered();
        var pod = Submitted(d);
        var reviewer = Guid.NewGuid();

        pod.RecordFieldEdit("Delivered Quantity", "95", "100", "Paper misread", reviewer, Now);

        var edit = pod.Reviews.Single();
        (edit.Action, edit.FieldName, edit.OldValue, edit.NewValue, edit.Reason, edit.PerformedBy).ShouldBe(("FieldEdited", "Delivered Quantity", "95", "100", "Paper misread", reviewer));
    }

    [Fact]
    public void Auto_acceptance_needs_every_check_to_pass_and_never_covers_a_discrepancy_unless_allowed()
    {
        var clean = Delivered();
        var pod = Submitted(clean);
        pod.ApplyValidation(Check(pod, clean).Select(c => (c.Type, c.Check, c.Status, c.Message)), Now);
        var rules = new AutoAcceptSetting(true, false, false);

        AutoAcceptPolicy.CanAutoAccept(pod, clean, rules, false).ShouldBeTrue();
        AutoAcceptPolicy.CanAutoAccept(pod, clean, rules with { Enabled = false }, false).ShouldBeFalse();
        AutoAcceptPolicy.CanAutoAccept(pod, clean, rules with { RequireOcr = true }, false).ShouldBeFalse();
        AutoAcceptPolicy.CanAutoAccept(pod, clean, rules with { RequireOcr = true }, true).ShouldBeTrue();

        var short3 = Arrived();
        DeliveryTestData.Complete(short3, DeliveryOutcome.Shortage, Qty(short3, 97, @short: 3));
        var shortPod = Submitted(short3);
        shortPod.ApplyValidation([("Business", "Discrepancy", ValidationOutcome.Warning, "recorded")], Now); // even when the checks pass
        AutoAcceptPolicy.CanAutoAccept(shortPod, short3, rules, true).ShouldBeFalse();
        AutoAcceptPolicy.CanAutoAccept(shortPod, short3, rules with { AllowWithDiscrepancy = true }, true).ShouldBeFalse(); // a Warning is not Valid
    }
}
