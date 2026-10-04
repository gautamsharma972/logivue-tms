using Tms.Modules.Transporters.Domain;

namespace Tms.UnitTests.Transporters;

public class ContactsAndBranchesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Carrier = Guid.NewGuid();

    [Fact]
    public void A_contact_needs_a_name_a_purpose_and_a_way_to_reach_them()
    {
        TransporterContact.Create(Tenant, Carrier, " ", null, null, "9876543210", "Operations", false).Error.ValidationErrors!.ShouldContainKey("name");
        TransporterContact.Create(Tenant, Carrier, "Anil", null, null, "9876543210", "", false).Error.ValidationErrors!.ShouldContainKey("contactType");
        TransporterContact.Create(Tenant, Carrier, "Anil", null, null, null, "Operations", false).Error.ValidationErrors!.ShouldContainKey("phone");
        TransporterContact.Create(Tenant, Carrier, "Anil", null, "not-an-email", null, "Operations", false).Error.ValidationErrors!.ShouldContainKey("email");
        TransporterContact.Create(Tenant, Carrier, "Anil", null, null, "12345", "Operations", false).Error.ValidationErrors!.ShouldContainKey("phone");

        var ok = TransporterContact.Create(Tenant, Carrier, "  Anil Kumar ", "Dispatch head", "anil@shree.example", "+91 98765 43210", "Operations", true).Value;
        ok.Name.ShouldBe("Anil Kumar");
        ok.Phone.ShouldBe("9876543210");
        ok.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void An_inactive_contact_cannot_be_the_primary_one()
    {
        var contact = TransporterContact.Create(Tenant, Carrier, "Anil", null, "a@b.example", null, "Operations", true).Value;

        contact.Set("Anil", null, "a@b.example", null, "Operations", isPrimary: true, isActive: false).Error.ValidationErrors!.ShouldContainKey("isPrimary");
        contact.Set("Anil", null, "a@b.example", null, "Operations", isPrimary: false, isActive: false).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_branch_has_a_code_a_name_and_a_location_inside_india_given_as_a_pair()
    {
        TransporterBranch.Create(Tenant, Carrier, "x", "Pune hub", null, null, null, null, null, null, null).Error.ValidationErrors!.ShouldContainKey("code");
        TransporterBranch.Create(Tenant, Carrier, "PNQ-1", " ", null, null, null, null, null, null, null).Error.ValidationErrors!.ShouldContainKey("name");
        TransporterBranch.Create(Tenant, Carrier, "PNQ-1", "Pune hub", null, null, null, 18.5, null, null, null).Error.ValidationErrors!.ShouldContainKey("latitude");
        TransporterBranch.Create(Tenant, Carrier, "PNQ-1", "Pune hub", null, null, null, 73.8, 18.5, null, null).Error.ValidationErrors!.ShouldContainKey("latitude"); // swapped

        var ok = TransporterBranch.Create(Tenant, Carrier, "pnq-1", "Pune hub", "MIDC", "Pune", "Maharashtra", 18.5, 73.8, "Ravi", "98765 43210").Value;
        ok.Code.ShouldBe("PNQ-1");
        ok.ContactPhone.ShouldBe("9876543210");
    }
}
