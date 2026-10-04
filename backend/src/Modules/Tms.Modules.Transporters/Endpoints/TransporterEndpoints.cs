using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Application.Documents;
using Tms.Modules.Transporters.Application.Fleet;
using Tms.Modules.Transporters.Application.Operations;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Application.Selection;
using Tms.Modules.Transporters.Application.Transporters;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Transporters.Endpoints;

/// <summary>
/// All routes require sign-in; what a caller may see or change (internal staff vs. a vendor's own company) is decided
/// inside the handlers by <see cref="TransporterAccess"/>.
/// </summary>
internal static class TransporterEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Transporters").RequireAuthorization();

        MapTransporters(api);
        MapFleet(api);
        MapDocuments(api);
        MapPerformance(api);
    }

    private static void MapPerformance(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/transporters");

        group.MapGet("/{id:guid}/performance", async (Guid id, DateOnly from, DateOnly to, PerformanceHandler h, CancellationToken ct) => (await h.GetAsync(id, from, to, ct)).ToHttpResult())
            .WithName("TransporterPerformance").Produces<PerformanceDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/performance/recalculate", async (Guid id, PeriodRequest body, PerformanceHandler h, CancellationToken ct) => (await h.RecalculateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<PeriodRequest>().WithName("RecalculateTransporterPerformance").Produces<IReadOnlyList<OperationalPeriodResult>>();

        group.MapPost("/performance/recalculate", async (PeriodRequest body, PerformanceHandler h, CancellationToken ct) => (await h.RecalculateAllAsync(body, ct)).ToHttpResult())
            .WithValidation<PeriodRequest>().WithName("RecalculateAllPerformance").Produces<int>();

        group.MapGet("/{id:guid}/scorecards", async (Guid id, ScorecardHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListScorecards").Produces<IReadOnlyList<ScorecardDto>>();

        group.MapPost("/{id:guid}/scorecards", async (Guid id, PeriodRequest body, ScorecardHandler h, CancellationToken ct) =>
                (await h.GenerateAsync(id, body, ct)).ToCreatedResult(s => $"/api/v1/transporters/{id}/scorecards"))
            .WithValidation<PeriodRequest>().WithName("GenerateScorecard").Produces<ScorecardDto>(StatusCodes.Status201Created);

        group.MapGet("/rankings", async ([AsParameters] RankingQuery query, RankingHandler h, CancellationToken ct) => (await h.RankAsync(query, ct)).ToHttpResult())
            .WithName("RankTransporters").Produces<RankingResultDto>();

        group.MapGet("/{id:guid}/benchmark", async (Guid id, [AsParameters] BenchmarkQuery query, RankingHandler h, CancellationToken ct) => (await h.BenchmarkAsync(id, query, ct)).ToHttpResult())
            .WithName("BenchmarkTransporter").Produces<BenchmarkDto>();

        group.MapGet("/{id:guid}/lanes", async (Guid id, LaneHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListTransporterLanes").Produces<IReadOnlyList<LaneDto>>();

        group.MapPost("/{id:guid}/lanes", async (Guid id, SaveLaneRequest body, LaneHandler h, CancellationToken ct) =>
                (await h.CreateAsync(id, body, ct)).ToCreatedResult(l => $"/api/v1/lanes/{l.Id}"))
            .WithName("CreateTransporterLane").Produces<LaneDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/lanes/{laneId:guid}", async (Guid laneId, SaveLaneRequest body, LaneHandler h, CancellationToken ct) => (await h.UpdateAsync(laneId, body, ct)).ToHttpResult())
            .WithName("UpdateTransporterLane").Produces<LaneDto>().ProducesValidationProblem();

        group.MapGet("/{id:guid}/executions", async (Guid id, ExecutionHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListTransporterExecutions").Produces<IReadOnlyList<ExecutionDto>>();

        group.MapPost("/eligibility", async (SelectionRequest body, SelectionHandler h, CancellationToken ct) => (await h.EligibilityAsync(body, ct)).ToHttpResult())
            .WithName("CheckTransporterEligibility").Produces<IReadOnlyList<CandidateEvaluation>>().ProducesValidationProblem();

        group.MapPost("/recommendation", async (SelectionRequest body, SelectionHandler h, CancellationToken ct) => (await h.RecommendAsync(body, ct)).ToHttpResult())
            .WithName("RecommendTransporter").Produces<RecommendationResult>().ProducesValidationProblem();

        group.MapGet("/capability-catalog", () => Results.Ok(CapabilityHandler.Catalog())).WithName("CapabilityCatalog").Produces<IReadOnlyList<CapabilityTypeDto>>();

        group.MapGet("/{id:guid}/capabilities", async (Guid id, CapabilityHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListTransporterCapabilities").Produces<IReadOnlyList<CapabilityDto>>();

        group.MapPost("/{id:guid}/capabilities", async (Guid id, AddCapabilityRequest body, CapabilityHandler h, CancellationToken ct) =>
                (await h.AddAsync(id, body, ct)).ToCreatedResult(c => $"/api/v1/capabilities/{c.Id}"))
            .WithName("AddTransporterCapability").Produces<CapabilityDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPost("/capabilities/{capabilityId:guid}/end", async (Guid capabilityId, CapabilityHandler h, CancellationToken ct) => (await h.EndAsync(capabilityId, ct)).ToHttpResult())
            .WithName("EndTransporterCapability").Produces<CapabilityDto>();

        group.MapGet("/{id:guid}/planning-rules", async (Guid id, PlanningRuleHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListPlanningRules").Produces<IReadOnlyList<PlanningRuleDto>>();

        group.MapPost("/{id:guid}/planning-rules", async (Guid id, AddPlanningRuleRequest body, PlanningRuleHandler h, CancellationToken ct) =>
                (await h.AddAsync(id, body, ct)).ToCreatedResult(r => $"/api/v1/planning-rules/{r.Id}"))
            .WithName("AddPlanningRule").Produces<PlanningRuleDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPost("/planning-rules/{ruleId:guid}/end", async (Guid ruleId, EndPlanningRuleRequest body, PlanningRuleHandler h, CancellationToken ct) => (await h.EndAsync(ruleId, body, ct)).ToHttpResult())
            .WithName("EndPlanningRule").Produces<PlanningRuleDto>().ProducesValidationProblem();

        var executions = api.MapGroup("/executions");
        executions.MapGet("/{id:guid}", async (Guid id, ExecutionHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .WithName("GetExecution").Produces<ExecutionDto>().ProducesProblem(StatusCodes.Status404NotFound);
        executions.MapPost("/", async (CreateExecutionRequest body, ExecutionHandler h, CancellationToken ct) =>
                (await h.CreateAsync(body, ct)).ToCreatedResult(e => $"/api/v1/executions/{e.Id}"))
            .WithName("CreateExecution").Produces<ExecutionDto>(StatusCodes.Status201Created);
        executions.MapPost("/{id:guid}/events", async (Guid id, RecordExecutionEventRequest body, ExecutionHandler h, CancellationToken ct) => (await h.RecordAsync(id, body, ct)).ToHttpResult())
            .WithValidation<RecordExecutionEventRequest>().WithName("RecordExecutionEvent").Produces<ExecutionDto>();
        executions.MapPost("/{id:guid}/delay", async (Guid id, AttributeDelayRequest body, ExecutionHandler h, CancellationToken ct) => (await h.AttributeAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AttributeDelayRequest>().WithName("AttributeExecutionDelay").Produces<ExecutionDto>();

        var placements = api.MapGroup("/placements");
        placements.MapGet("/", async ([AsParameters] ListPlacementsQuery query, PlacementHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult())
            .WithName("ListPlacements").Produces<PagedResult<PlacementDto>>();
        placements.MapGet("/{id:guid}", async (Guid id, PlacementHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .WithName("GetPlacement").Produces<PlacementDto>().ProducesProblem(StatusCodes.Status404NotFound);
        placements.MapPost("/{id:guid}/report", async (Guid id, PlacementHandler h, CancellationToken ct) => (await h.ReportAsync(id, ct)).ToHttpResult())
            .WithName("ReportPlacement").Produces<PlacementDto>();
        placements.MapPost("/{id:guid}/place", async (Guid id, PlacementHandler h, CancellationToken ct) => (await h.PlaceAsync(id, ct)).ToHttpResult())
            .WithName("PlacePlacement").Produces<PlacementDto>();
        placements.MapPost("/{id:guid}/no-show", async (Guid id, ReasonRequest body, PlacementHandler h, CancellationToken ct) => (await h.NoShowAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("PlacementNoShow").Produces<PlacementDto>();
        placements.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, PlacementHandler h, CancellationToken ct) => (await h.CancelAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("CancelPlacement").Produces<PlacementDto>();

        group.MapGet("/{id:guid}/claims", async (Guid id, DateOnly from, DateOnly to, ClaimHandler h, CancellationToken ct) => (await h.ListAsync(id, from, to, ct)).ToHttpResult())
            .WithName("ListTransporterClaims").Produces<IReadOnlyList<ClaimDto>>();
        group.MapPost("/{id:guid}/claims", async (Guid id, RecordClaimRequest body, ClaimHandler h, CancellationToken ct) =>
                (await h.RecordAsync(id, body, ct)).ToCreatedResult(c => $"/api/v1/claims/{c.Id}"))
            .WithName("RecordTransporterClaim").Produces<ClaimDto>(StatusCodes.Status201Created).ProducesValidationProblem();
        api.MapPost("/claims/{claimId:guid}/resolve", async (Guid claimId, ClaimHandler h, CancellationToken ct) => (await h.ResolveAsync(claimId, ct)).ToHttpResult())
            .WithName("ResolveTransporterClaim").Produces<ClaimDto>();
        api.MapPut("/claims/{claimId:guid}/value", async (Guid claimId, SetClaimValueRequest body, ClaimHandler h, CancellationToken ct) => (await h.SetValueAsync(claimId, body, ct)).ToHttpResult())
            .WithName("SetTransporterClaimValue").Produces<ClaimDto>().ProducesValidationProblem();

        group.MapGet("/{id:guid}/costs", async (Guid id, DateOnly from, DateOnly to, LoadCostHandler h, CancellationToken ct) => (await h.ListAsync(id, from, to, ct)).ToHttpResult())
            .WithName("ListTransporterCosts").Produces<IReadOnlyList<LoadCostDto>>();
        group.MapPost("/{id:guid}/costs", async (Guid id, RecordLoadCostRequest body, LoadCostHandler h, CancellationToken ct) =>
                (await h.RecordAsync(id, body, ct)).ToCreatedResult(c => $"/api/v1/transporters/{id}/costs"))
            .WithValidation<RecordLoadCostRequest>().WithName("RecordTransporterCost").Produces<LoadCostDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}/capacity", async (Guid id, DateOnly from, DateOnly to, CapacityHandler h, CancellationToken ct) => (await h.ListAsync(id, from, to, ct)).ToHttpResult())
            .WithName("ListTransporterCapacity").Produces<IReadOnlyList<CapacityDayDto>>();
        group.MapPut("/{id:guid}/capacity", async (Guid id, SaveCapacityRequest body, CapacityHandler h, CancellationToken ct) => (await h.SaveAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveCapacityRequest>().WithName("SaveTransporterCapacity").Produces<CapacityDayDto>().ProducesValidationProblem();

        var alerts = api.MapGroup("/transporter-alerts");
        alerts.MapGet("/", async ([AsParameters] ListAlertsQuery query, AlertHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult())
            .WithName("ListTransporterAlerts").Produces<PagedResult<AlertDto>>();
        alerts.MapPost("/evaluate", async (AlertHandler h, CancellationToken ct) => (await h.EvaluateAsync(ct)).ToHttpResult())
            .WithName("EvaluateTransporterAlerts").Produces<int>();
        alerts.MapPost("/{id:guid}/acknowledge", async (Guid id, AlertHandler h, CancellationToken ct) => (await h.AcknowledgeAsync(id, ct)).ToHttpResult())
            .WithName("AcknowledgeTransporterAlert").Produces<AlertDto>();
        alerts.MapPost("/{id:guid}/resolve", async (Guid id, ResolveAlertRequest body, AlertHandler h, CancellationToken ct) => (await h.ResolveAsync(id, body, ct)).ToHttpResult())
            .WithName("ResolveTransporterAlert").Produces<AlertDto>();

        var settings = api.MapGroup("/transporter-settings");
        settings.MapGet("/", async (SettingsHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult())
            .WithName("ListTransporterSettings").Produces<IReadOnlyList<SettingDto>>();
        settings.MapPut("/{key}", async (string key, SaveSettingRequest body, SettingsHandler h, CancellationToken ct) => (await h.SaveAsync(key, body, ct)).ToHttpResult())
            .WithName("SaveTransporterSetting").Produces<SettingDto>().ProducesValidationProblem();
    }

    private static void MapTransporters(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/transporters");

        group.MapGet("/", async ([AsParameters] ListTransportersQuery query, ListTransportersHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListTransporters").Produces<PagedResult<TransporterSummaryDto>>();

        group.MapGet("/lookup", async (string? search, LookupTransportersHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(search, ct)).ToHttpResult())
            .WithName("LookupTransporters").Produces<IReadOnlyList<TransporterLookupDto>>();

        group.MapPost("/", async (SaveTransporterRequest body, CreateTransporterHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToCreatedResult(t => $"/api/v1/transporters/{t.Id}"))
            .WithValidation<SaveTransporterRequest>()
            .WithName("CreateTransporter").Produces<TransporterDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetTransporterHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetTransporter").Produces<TransporterDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, SaveTransporterRequest body, UpdateTransporterHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveTransporterRequest>()
            .WithName("UpdateTransporter").Produces<TransporterDto>().ProducesValidationProblem();

        group.MapPut("/{id:guid}/bank", async (Guid id, SaveBankRequest body, UpdateBankHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveBankRequest>()
            .WithName("UpdateTransporterBank").Produces<TransporterDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/submit", async (Guid id, SubmitTransporterHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("SubmitTransporter").Produces<TransporterDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/suspend", async (Guid id, SuspendRequest body, SuspendTransporterHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SuspendRequest>()
            .WithName("SuspendTransporter").Produces<TransporterDto>();

        group.MapPost("/{id:guid}/reactivate", async (Guid id, SuspendTransporterHandler handler, CancellationToken ct) =>
                (await handler.ReactivateAsync(id, ct)).ToHttpResult())
            .WithName("ReactivateTransporter").Produces<TransporterDto>();

        group.MapGet("/compliance", async ([AsParameters] ComplianceReportQuery query, ComplianceReportHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ComplianceReport").Produces<PagedResult<ComplianceItemDto>>();
    }

    private static void MapFleet(RouteGroupBuilder api)
    {
        var types = api.MapGroup("/vehicle-types");
        types.MapGet("/", async (ListVehicleTypesHandler handler, CancellationToken ct) => (await handler.HandleAsync(ct)).ToHttpResult())
            .WithName("ListVehicleTypes").Produces<IReadOnlyList<VehicleTypeDto>>();
        types.MapPost("/", async (SaveVehicleTypeRequest body, SaveVehicleTypeHandler handler, CancellationToken ct) =>
                (await handler.CreateAsync(body, ct)).ToCreatedResult(t => $"/api/v1/vehicle-types/{t.Id}"))
            .WithValidation<SaveVehicleTypeRequest>().WithName("CreateVehicleType").Produces<VehicleTypeDto>(StatusCodes.Status201Created);
        types.MapPut("/{id:guid}", async (Guid id, SaveVehicleTypeRequest body, SaveVehicleTypeHandler handler, CancellationToken ct) =>
                (await handler.UpdateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveVehicleTypeRequest>().WithName("UpdateVehicleType").Produces<VehicleTypeDto>();

        api.MapGet("/transporters/{id:guid}/vehicles", async (Guid id, string? search, VehicleHandler handler, CancellationToken ct, int page = 1, int pageSize = 25) =>
                (await handler.ListAsync(id, search, page, pageSize, ct)).ToHttpResult())
            .WithName("ListVehicles").Produces<PagedResult<VehicleDto>>();
        api.MapPost("/transporters/{id:guid}/vehicles", async (Guid id, SaveVehicleRequest body, VehicleHandler handler, CancellationToken ct) =>
                (await handler.CreateAsync(id, body, ct)).ToCreatedResult(v => $"/api/v1/vehicles/{v.Id}"))
            .WithValidation<SaveVehicleRequest>().WithName("CreateVehicle").Produces<VehicleDto>(StatusCodes.Status201Created).ProducesValidationProblem();
        api.MapPut("/vehicles/{vehicleId:guid}", async (Guid vehicleId, SaveVehicleRequest body, VehicleHandler handler, CancellationToken ct) =>
                (await handler.UpdateAsync(vehicleId, body, ct)).ToHttpResult())
            .WithValidation<SaveVehicleRequest>().WithName("UpdateVehicle").Produces<VehicleDto>().ProducesValidationProblem();

        api.MapGet("/transporters/{id:guid}/drivers", async (Guid id, string? search, DriverHandler handler, CancellationToken ct, int page = 1, int pageSize = 25) =>
                (await handler.ListAsync(id, search, page, pageSize, ct)).ToHttpResult())
            .WithName("ListDrivers").Produces<PagedResult<DriverDto>>();
        api.MapPost("/transporters/{id:guid}/drivers", async (Guid id, SaveDriverRequest body, DriverHandler handler, CancellationToken ct) =>
                (await handler.CreateAsync(id, body, ct)).ToCreatedResult(d => $"/api/v1/drivers/{d.Id}"))
            .WithValidation<SaveDriverRequest>().WithName("CreateDriver").Produces<DriverDto>(StatusCodes.Status201Created).ProducesValidationProblem();
        api.MapPut("/drivers/{driverId:guid}", async (Guid driverId, SaveDriverRequest body, DriverHandler handler, CancellationToken ct) =>
                (await handler.UpdateAsync(driverId, body, ct)).ToHttpResult())
            .WithValidation<SaveDriverRequest>().WithName("UpdateDriver").Produces<DriverDto>().ProducesValidationProblem();
    }

    private static void MapDocuments(RouteGroupBuilder api)
    {
        api.MapGet("/transporters/{id:guid}/documents",
                async (Guid id, OwnerKind? ownerKind, Guid? ownerId, bool? includeSuperseded, DocumentHandler handler, CancellationToken ct) =>
                    (await handler.ListAsync(id, ownerKind, ownerId, includeSuperseded ?? false, ct)).ToHttpResult())
            .WithName("ListDocuments").Produces<IReadOnlyList<DocumentDto>>();

        api.MapPost("/transporters/{id:guid}/documents",
                async (Guid id, [FromForm] UploadDocumentForm form, DocumentHandler handler, CancellationToken ct) =>
                    (await handler.UploadAsync(id, form, ct)).ToCreatedResult(d => $"/api/v1/documents/{d.Id}"))
            .DisableAntiforgery() // bearer-token API: there is no cookie session for a CSRF attack to ride on
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024))
            .WithName("UploadDocument").Accepts<UploadDocumentForm>("multipart/form-data")
            .Produces<DocumentDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapGet("/documents/{documentId:guid}/file", async (Guid documentId, DocumentHandler handler, CancellationToken ct) =>
            {
                var result = await handler.DownloadAsync(documentId, ct);
                return result.IsSuccess
                    ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
                    : result.Error.ToProblem();
            })
            .WithName("DownloadDocument").Produces(StatusCodes.Status200OK);

        api.MapDelete("/documents/{documentId:guid}", async (Guid documentId, DocumentHandler handler, CancellationToken ct) =>
                (await handler.DeleteAsync(documentId, ct)).ToHttpResult())
            .WithName("DeleteDocument").Produces(StatusCodes.Status204NoContent);
    }
}
