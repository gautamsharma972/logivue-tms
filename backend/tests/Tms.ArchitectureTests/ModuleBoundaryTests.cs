using System.Reflection;
using NetArchTest.Rules;
using Tms.BuildingBlocks.Web;
using Tms.Modules.Platform;

namespace Tms.ArchitectureTests;

/// <summary>
/// Executable architecture rules. Add each new module's marker type to <see cref="ModuleAssemblies"/>
/// and the same rules apply to it automatically.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly SharedKernel = typeof(Tms.SharedKernel.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly WebBuildingBlocks = typeof(Tms.BuildingBlocks.Web.ServiceCollectionExtensions).Assembly;

    public static TheoryData<Assembly> ModuleAssemblies => new() { typeof(PlatformModule).Assembly, typeof(Tms.Modules.Approvals.ApprovalsModule).Assembly, typeof(Tms.Modules.Transporters.Domain.Transporter).Assembly, typeof(Tms.Modules.Contracts.Domain.Contract).Assembly, typeof(Tms.Modules.Shipments.Domain.Shipment).Assembly, typeof(Tms.Modules.Deliveries.Domain.Delivery).Assembly, typeof(Tms.Modules.Tracking.Domain.TrackedShipment).Assembly };

    [Fact]
    public void SharedKernel_DependsOnNoModuleAndNoWebCode()
    {
        var result = Types.InAssembly(SharedKernel)
            .ShouldNot()
            .HaveDependencyOnAny("Tms.Modules", "Tms.BuildingBlocks.Web", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void WebBuildingBlocks_DependsOnNoModule()
    {
        var result = Types.InAssembly(WebBuildingBlocks).ShouldNot().HaveDependencyOnAny("Tms.Modules").GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Modules_DoNotReferenceOtherModules(Assembly module)
    {
        var own = module.GetName().Name!;
        var foreign = module.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Tms.Modules.", StringComparison.Ordinal) && n != own)
            .ToList();

        foreign.ShouldBeEmpty($"{own} must talk to other modules through events or contracts, not direct references.");
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Domain_IsIndependentOfInfrastructureAndFrameworks(Assembly module)
    {
        var ns = module.GetName().Name + ".Domain";

        Types.InAssembly(module).That().ResideInNamespaceStartingWith(ns).GetTypes()
            .ShouldNotBeEmpty("the rule would otherwise pass vacuously");

        var result = Types.InAssembly(module)
            .That().ResideInNamespaceStartingWith(ns)
            .ShouldNot()
            .HaveDependencyOnAny(
                module.GetName().Name + ".Application",
                module.GetName().Name + ".Infrastructure",
                module.GetName().Name + ".Endpoints",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Application_DoesNotDependOnEndpoints(Assembly module)
    {
        var name = module.GetName().Name;

        var result = Types.InAssembly(module)
            .That().ResideInNamespaceStartingWith(name + ".Application")
            .ShouldNot().HaveDependencyOn(name + ".Endpoints")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Handlers_AreInternal(Assembly module)
    {
        var result = Types.InAssembly(module)
            .That().HaveNameEndingWith("Handler").And().AreClasses()
            .Should().NotBePublic()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } ? "Violations: " + string.Join(", ", result.FailingTypeNames) : string.Empty;
}
