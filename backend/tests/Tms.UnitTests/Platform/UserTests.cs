using Tms.Modules.Platform.Domain;

namespace Tms.UnitTests.Platform;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static User NewUser(params Role[] roles) =>
        User.Create(Guid.NewGuid(), "  Jane.Doe@Example.COM ", " Jane Doe ", "hash", UserType.Internal, roles);

    [Fact]
    public void Create_NormalisesEmail_AndRaisesEvent()
    {
        var user = NewUser();

        user.Email.ShouldBe("jane.doe@example.com");
        user.FullName.ShouldBe("Jane Doe");
        user.IsActive.ShouldBeTrue();
        user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserCreated>().UserId.ShouldBe(user.Id);
    }

    [Fact]
    public void FifthFailedLogin_LocksTheAccount_ForTheLockoutDuration()
    {
        var user = NewUser();

        for (var i = 0; i < User.MaxFailedLogins - 1; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.IsLockedOut(Now).ShouldBeFalse();

        user.RecordFailedLogin(Now);

        user.IsLockedOut(Now).ShouldBeTrue();
        user.IsLockedOut(Now + User.LockoutDuration - TimeSpan.FromSeconds(1)).ShouldBeTrue();
        user.IsLockedOut(Now + User.LockoutDuration).ShouldBeFalse();
    }

    [Fact]
    public void SuccessfulLogin_ClearsFailuresAndLock()
    {
        var user = NewUser();
        for (var i = 0; i < User.MaxFailedLogins; i++)
        {
            user.RecordFailedLogin(Now);
        }

        user.RecordSuccessfulLogin(Now);

        user.IsLockedOut(Now).ShouldBeFalse();
        user.FailedLoginCount.ShouldBe(0);
        user.LastLoginAt.ShouldBe(Now);
    }

    [Fact]
    public void EffectivePermissions_IsTheUnionOfAllRoles()
    {
        var tenant = Guid.NewGuid();
        var a = Role.Create(tenant, "A", null, ["users.read", "roles.read"]);
        var b = Role.Create(tenant, "B", null, ["users.read", "audit.read"]);

        NewUser(a, b).EffectivePermissions.OrderBy(p => p).ShouldBe(["audit.read", "roles.read", "users.read"]);
    }

    [Fact]
    public void SetRoles_ReplacesAssignmentsWithoutDuplicates()
    {
        var tenant = Guid.NewGuid();
        var a = Role.Create(tenant, "A", null, []);
        var b = Role.Create(tenant, "B", null, []);
        var user = NewUser(a);

        user.SetRoles([a, b]);
        user.Roles.Count.ShouldBe(2);

        user.SetRoles([b]);
        user.Roles.ShouldHaveSingleItem().Id.ShouldBe(b.Id);
    }
}
