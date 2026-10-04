using FluentValidation;
using LogiVue.Tms.TransporterManagement.Api.Controllers;
using LogiVue.Tms.TransporterManagement.Application.Alerts;
using LogiVue.Tms.TransporterManagement.Application.Claims;
using LogiVue.Tms.TransporterManagement.Application.Ranking;
using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Execution;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Planning;
using LogiVue.Tms.TransporterManagement.Application.Monitoring;
using LogiVue.Tms.TransporterManagement.Application.Pod;
using LogiVue.Tms.TransporterManagement.Application.Scorecards;
using LogiVue.Tms.TransporterManagement.Application.Recommendation;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Coverage;
using LogiVue.Tms.TransporterManagement.Application.Documents;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Application.Fleet;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace LogiVue.Tms.TransporterManagement.Api;

public static class TransporterManagementApi
{
    /// <summary>
    /// Registers the module's application services, validators and controllers. The host maps its own
    /// endpoints, middleware and authentication; the module does not register those.
    /// </summary>
    public static IMvcBuilder AddTransporterManagementApi(this IServiceCollection services)
    {
        services.AddScoped<ITransporterService, TransporterService>();
        services.AddScoped<IFleetService, FleetService>();
        services.AddScoped<IDriverService, DriverService>();
        services.AddScoped<ICoverageService, CoverageService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<IComplianceService, ComplianceService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<ITransporterEligibilityService, TransporterEligibilityService>();
        services.AddScoped<ITransporterRecommendationService, TransporterRecommendationService>();
        services.AddScoped<ITransporterPlanningIntegration, LocalPlanningIntegration>();
        services.AddScoped<IPlanningRuleService, PlanningRuleService>();
        services.AddScoped<ITenderService, TenderService>();
        services.AddScoped<ITenderResponseService, TenderResponseService>();
        services.AddScoped<ITenderLifecycleService, TenderLifecycleService>();
        services.AddScoped<IPlacementService, PlacementService>();
        services.AddScoped<IExecutionService, ExecutionService>();
        services.AddScoped<IPodService, PodService>();
        services.AddScoped<IPerformanceService, PerformanceService>();
        services.AddScoped<IScorecardService, ScorecardService>();
        services.AddScoped<IOperationalMonitor, OperationalMonitor>();
        services.AddScoped<IClaimService, ClaimService>();
        services.AddScoped<ILoadCostService, LoadCostService>();
        services.AddScoped<ICapacityService, CapacityService>();
        services.AddScoped<IRankingService, RankingService>();

        services.AddValidatorsFromAssemblyContaining<CreateTransporterRequestValidator>();

        return services
            .AddControllers()
            .AddApplicationPart(typeof(TransporterManagementInfoController).Assembly);
    }
}
