using Tms.Modules.Platform.Application;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.UnitTests.Platform;

public class RoleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_DeduplicatesAndSortsPermissions()
    {
        var role = Role.Create(Tenant, " Ops ", null, ["b.read", "a.read", "b.read", " "]);

        role.Name.ShouldBe("Ops");
        role.Permissions.ShouldBe(["a.read", "b.read"]);
    }

    [Fact]
    public void Update_OnSystemRole_IsRejected()
    {
        var role = Role.Create(Tenant, Role.AdministratorName, null, ["a.read"], isSystem: true);

        var result = role.Update("Renamed", null, []);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        role.Name.ShouldBe(Role.AdministratorName);
    }

    [Fact]
    public void SyncSystemPermissions_PicksUpNewModulePermissions_ButLeavesCustomRolesAlone()
    {
        var system = Role.Create(Tenant, "Administrator", null, ["a.read"], isSystem: true);
        var custom = Role.Create(Tenant, "Custom", null, ["a.read"]);

        system.SyncSystemPermissions(["a.read", "b.read"]);
        custom.SyncSystemPermissions(["a.read", "b.read"]);

        system.Permissions.ShouldBe(["a.read", "b.read"]);
        custom.Permissions.ShouldBe(["a.read"]);
    }

    [Fact]
    public void RoleAssignmentPolicy_BlocksGrantingPermissionsTheCallerLacks()
    {
        var caller = Substitute.For<ICurrentUser>();
        caller.Permissions.Returns(new HashSet<string> { "users.read", "users.manage" });

        RoleAssignmentPolicy.CanGrant(caller, ["users.read"]).ShouldBeTrue();
        RoleAssignmentPolicy.CanGrant(caller, ["users.read", "roles.manage"]).ShouldBeFalse();
    }
}
