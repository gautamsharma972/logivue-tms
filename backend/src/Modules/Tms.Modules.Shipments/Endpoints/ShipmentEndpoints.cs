using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Shipments.Application;
using Microsoft.AspNetCore.Mvc;
using Tms.Modules.Shipments.Application.Delivery;
using Tms.Modules.Shipments.Application.Locations;
using Tms.Modules.Shipments.Application.MilkRuns;
using Tms.Modules.Shipments.Application.Orders;
using Tms.Modules.Shipments.Application.Planning;
using Tms.Modules.Shipments.Application.PlanningRuns;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Application.Shipments;
using Tms.Modules.Shipments.Application.Tendering;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Endpoints;

/// <summary>All routes require sign-in; permissions and the vendor scoping are enforced inside the handlers (<see cref="ShipmentAccess"/>).</summary>
internal static class ShipmentEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Shipments").RequireAuthorization();
        MapLocations(api);
        MapOrders(api);
        MapPlanning(api);
        MapShipments(api);
        MapDelivery(api);
    }

    private static void MapLocations(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/locations");

        group.MapGet("/", async ([AsParameters] ListLocationsQuery query, ListLocationsHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListLocations").Produces<PagedResult<LocationDto>>();

        group.MapPost("/", async (SaveLocationRequest body, SaveLocationHandler h, CancellationToken ct) =>
                (await h.CreateAsync(body, ct)).ToCreatedResult(l => $"/api/v1/locations/{l.Id}"))
            .WithValidation<SaveLocationRequest>().WithName("CreateLocation").Produces<LocationDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetLocationHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetLocation").Produces<LocationDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, SaveLocationRequest body, SaveLocationHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveLocationRequest>().WithName("UpdateLocation").Produces<LocationDto>().ProducesValidationProblem();

        group.MapPost("/distance", async (DistanceRequest body, DistanceHandler h, CancellationToken ct) => (await h.HandleAsync(body, ct)).ToHttpResult())
            .WithName("LocationDistance").Produces<DistanceDto>();
    }

    private static void MapOrders(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/orders");

        group.MapGet("/", async ([AsParameters] ListOrdersQuery query, ListOrdersHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListOrders").Produces<PagedResult<OrderDto>>();

        group.MapPost("/", async (SaveOrderRequest body, CreateOrderHandler h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToCreatedResult(o => $"/api/v1/orders/{o.Id}"))
            .WithValidation<SaveOrderRequest>().WithName("CreateOrder").Produces<OrderDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetOrderHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetOrder").Produces<OrderDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, SaveOrderRequest body, UpdateOrderHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveOrderRequest>().WithName("UpdateOrder").Produces<OrderDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, CancelOrderHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("CancelOrder").Produces<OrderDto>();
    }

    private static void MapPlanning(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/planning");

        group.MapPost("/advice", async (AdviceRequest body, AdviceHandler h, CancellationToken ct) => (await h.HandleAsync(body, ct)).ToHttpResult())
            .WithValidation<AdviceRequest>().WithName("PlanningAdvice").Produces<AdviceDto>().ProducesValidationProblem();

        group.MapGet("/vehicle-types", async (ListVehicleTypesHandler h, CancellationToken ct) => (await h.HandleAsync(ct)).ToHttpResult())
            .WithName("PlanningVehicleTypes").Produces<IReadOnlyList<Tms.SharedKernel.Contracts.VehicleTypeInfo>>();

        group.MapGet("/compatibility-rules", async (CompatibilityRulesHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult())
            .WithName("ListCompatibilityRules").Produces<IReadOnlyList<CompatibilityRuleDto>>();

        group.MapPost("/compatibility-rules", async (SaveCompatibilityRuleRequest body, CompatibilityRulesHandler h, CancellationToken ct) =>
                (await h.CreateAsync(body, ct)).ToCreatedResult(r => $"/api/v1/planning/compatibility-rules/{r.Id}"))
            .WithValidation<SaveCompatibilityRuleRequest>().WithName("CreateCompatibilityRule").Produces<CompatibilityRuleDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapDelete("/compatibility-rules/{id:guid}", async (Guid id, CompatibilityRulesHandler h, CancellationToken ct) => (await h.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteCompatibilityRule").Produces(StatusCodes.Status204NoContent);

        group.MapGet("/suggestions", async (SuggestLoadsHandler h, CancellationToken ct) => (await h.HandleAsync(ct)).ToHttpResult())
            .WithName("PlanningSuggestions").Produces<IReadOnlyList<SuggestedLoadDto>>();

        group.MapPost("/consolidation/preview", async (CreateRunRequest body, RunViewsHandler h, CancellationToken ct) => (await h.ConsolidationAsync(body, ct)).ToHttpResult())
            .WithValidation<CreateRunRequest>().WithName("PreviewConsolidation").Produces<ConsolidationPreviewDto>().ProducesValidationProblem();

        group.MapPost("/returns/plan", async (CreateRunRequest body, RunViewsHandler h, CancellationToken ct) => (await h.ReturnsAsync(body, ct)).ToHttpResult())
            .WithValidation<CreateRunRequest>().WithName("PlanReturns").Produces<ReturnsPlanDto>().ProducesValidationProblem();

        group.MapPost("/preview", async (CreateRunRequest body, PreviewRunHandler h, CancellationToken ct) => (await h.HandleAsync(body, ct)).ToHttpResult())
            .WithValidation<CreateRunRequest>().WithName("PreviewPlan").Produces<PlanSnapshot>().ProducesValidationProblem();

        group.MapGet("/orders/unplanned", async ([AsParameters] ListOrdersQuery query, ListOrdersHandler h, CancellationToken ct) =>
                (await h.HandleAsync(query with { Status = OrderStatus.Open }, ct)).ToHttpResult())
            .WithName("UnplannedOrders").Produces<PagedResult<OrderDto>>();

        group.MapGet("/vehicles/recommendations", async ([AsParameters] RecommendationQuery query, VehicleRecommendationHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("VehicleRecommendations").Produces<IReadOnlyList<VehicleTypeEvaluation>>();

        group.MapPost("/ftl-ptl/compare", async (CompareRequest body, CompareModesHandler h, CancellationToken ct) => (await h.HandleAsync(body, ct)).ToHttpResult())
            .WithValidation<CompareRequest>().WithName("CompareFtlPtl").Produces<ComparisonDto>();

        MapRuns(group);
        MapMilkRuns(group);

        group.MapGet("/dashboard", async ([AsParameters] DashboardQuery query, DashboardHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("PlanningDashboard").Produces<DashboardDto>().ProducesValidationProblem();

        group.MapGet("/dashboard/export", async ([AsParameters] DashboardQuery query, string? format, ExportHandler h, CancellationToken ct) =>
                AsFile(await h.DashboardAsync(query, format, ct)))
            .WithName("ExportPlanningDashboard").Produces<byte[]>().ProducesValidationProblem();

        group.MapGet("/utilization", async ([AsParameters] UtilizationQuery query, UtilizationHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("UtilizationReport").Produces<UtilizationDto>();
    }

    private static void MapDelivery(RouteGroupBuilder api)
    {
        var shipments = api.MapGroup("/shipments");

        shipments.MapPost("/{id:guid}/orders/{orderId:guid}/delivery", async (Guid id, Guid orderId, RecordDeliveryRequest body, DeliveryHandler h, CancellationToken ct) =>
                (await h.RecordAsync(id, orderId, body, ct)).ToHttpResult())
            .WithValidation<RecordDeliveryRequest>().WithName("RecordDelivery").Produces<ShipmentDto>().ProducesValidationProblem();

        shipments.MapGet("/{id:guid}/orders/{orderId:guid}/pod", async (Guid id, Guid orderId, DeliveryHandler h, CancellationToken ct) => (await h.ListDocumentsAsync(id, orderId, ct)).ToHttpResult())
            .WithName("ListPodDocuments").Produces<IReadOnlyList<PodDocumentDto>>();

        shipments.MapPost("/{id:guid}/orders/{orderId:guid}/pod", async (Guid id, Guid orderId, [FromForm] UploadPodForm form, DeliveryHandler h, CancellationToken ct) =>
                (await h.UploadAsync(id, orderId, form, ct)).ToCreatedResult(d => $"/api/v1/pod-documents/{d.Id}"))
            .DisableAntiforgery() // bearer-token API: there is no cookie session for a CSRF attack to ride on
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024))
            .WithName("UploadPod").Accepts<UploadPodForm>("multipart/form-data").Produces<PodDocumentDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        shipments.MapPost("/{id:guid}/orders/{orderId:guid}/pod/verify", async (Guid id, Guid orderId, DeliveryHandler h, CancellationToken ct) => (await h.VerifyAsync(id, orderId, ct)).ToHttpResult())
            .WithName("VerifyPod").Produces<ShipmentDto>();

        shipments.MapPost("/{id:guid}/orders/{orderId:guid}/pod/reject", async (Guid id, Guid orderId, ReasonRequest body, DeliveryHandler h, CancellationToken ct) => (await h.RejectAsync(id, orderId, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("RejectPod").Produces<ShipmentDto>();

        api.MapGet("/pod-documents/{documentId:guid}/file", async (Guid documentId, DeliveryHandler h, CancellationToken ct) =>
            {
                var result = await h.DownloadAsync(documentId, ct);
                return result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("DownloadPod").Produces(StatusCodes.Status200OK);

        api.MapDelete("/pod-documents/{documentId:guid}", async (Guid documentId, DeliveryHandler h, CancellationToken ct) => (await h.DeleteAsync(documentId, ct)).ToHttpResult())
            .WithName("DeletePodDocument").Produces(StatusCodes.Status204NoContent);

        api.MapGet("/pod", async ([AsParameters] ListPodQuery query, PodQueueHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListPod").Produces<PagedResult<PodLineDto>>();

        api.MapGet("/pod/ageing", async ([AsParameters] AgeingQuery query, AgeingHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("PodAgeing").Produces<AgeingDto>();
    }

    private static IResult AsFile(Result<ExportFile> result) =>
        result.IsFailure ? result.ToHttpResult() : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);

    private static void MapMilkRuns(RouteGroupBuilder planning)
    {
        var group = planning.MapGroup("/milk-runs");

        group.MapGet("/", async ([AsParameters] ListMilkRunsQuery query, ListMilkRunsHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListMilkRuns").Produces<PagedResult<MilkRunDto>>();

        group.MapPost("/", async (SaveMilkRunRequest body, SaveMilkRunHandler h, CancellationToken ct) =>
                (await h.CreateAsync(body, ct)).ToCreatedResult(m => $"/api/v1/planning/milk-runs/{m.Id}"))
            .WithValidation<SaveMilkRunRequest>().WithName("CreateMilkRun").Produces<MilkRunDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetMilkRunHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetMilkRun").Produces<MilkRunDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, SaveMilkRunRequest body, SaveMilkRunHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveMilkRunRequest>().WithName("UpdateMilkRun").Produces<MilkRunDto>().ProducesValidationProblem();

        group.MapPost("/preview", async (PlanMilkRunRequest body, PlanMilkRunHandler h, CancellationToken ct) => (await h.HandleAsync(body, ct)).ToHttpResult())
            .WithName("PreviewMilkRun").Produces<MilkRunPlan>();

        group.MapPost("/commit", async (CommitMilkRunRequest body, CommitMilkRunHandler h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToCreatedResult(_ => "/api/v1/shipments"))
            .WithName("CommitMilkRun").Produces<CommitMilkRunResultDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapRuns(RouteGroupBuilder planning)
    {
        var runs = planning.MapGroup("/runs");

        runs.MapGet("/", async ([AsParameters] ListRunsQuery query, ListRunsHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListPlanningRuns").Produces<PagedResult<RunSummaryDto>>();

        runs.MapPost("/", async (CreateRunRequest body, CreateRunHandler h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToCreatedResult(r => $"/api/v1/planning/runs/{r.Id}"))
            .WithValidation<CreateRunRequest>().WithName("CreatePlanningRun").Produces<RunDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        runs.MapGet("/{id:guid}", async (Guid id, GetRunHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetPlanningRun").Produces<RunDto>().ProducesProblem(StatusCodes.Status404NotFound);

        runs.MapGet("/{id:guid}/versions", async (Guid id, RunVersionsHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("PlanningRunVersions").Produces<IReadOnlyList<VersionDto>>();

        runs.MapGet("/{id:guid}/vehicles", async (Guid id, RunViewsHandler h, CancellationToken ct) => (await h.VehiclesAsync(id, ct)).ToHttpResult())
            .WithName("PlanningRunVehicles").Produces<IReadOnlyList<RunVehicleDto>>();

        runs.MapGet("/{id:guid}/stops", async (Guid id, RunViewsHandler h, CancellationToken ct) => (await h.StopsAsync(id, ct)).ToHttpResult())
            .WithName("PlanningRunStops").Produces<IReadOnlyList<RunStopDto>>();

        runs.MapPost("/{id:guid}/reoptimize", async (Guid id, ReoptimizeRequest body, ReoptimizeRunHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReoptimizeRequest>().WithName("ReoptimizePlanningRun").Produces<RunDto>();

        runs.MapGet("/{id:guid}/export", async (Guid id, string? format, ExportHandler h, CancellationToken ct) => AsFile(await h.RunAsync(id, format, ct)))
            .WithName("ExportPlanningRun").Produces<byte[]>().ProducesValidationProblem();

        runs.MapPost("/{id:guid}/edit", async (Guid id, EditPlanRequest body, EditRunHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<EditPlanRequest>().WithName("EditPlanningRun").Produces<RunDto>().ProducesProblem(StatusCodes.Status409Conflict);

        runs.MapPost("/{id:guid}/lock", async (Guid id, LockRequest body, LockVehicleHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithName("LockPlanVehicle").Produces<RunDto>();

        runs.MapPost("/{id:guid}/approve", async (Guid id, RunLifecycleHandler h, CancellationToken ct) => (await h.ApproveAsync(id, ct)).ToHttpResult())
            .WithName("ApprovePlanningRun").Produces<RunDto>();

        runs.MapPost("/{id:guid}/commit", async (Guid id, RunLifecycleHandler h, CancellationToken ct) => (await h.CommitAsync(id, ct)).ToHttpResult())
            .WithName("CommitPlanningRun").Produces<RunDto>();

        runs.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, RunLifecycleHandler h, CancellationToken ct) => (await h.CancelAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("CancelPlanningRun").Produces<RunDto>();
    }

    private static void MapShipments(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/shipments");

        group.MapGet("/", async ([AsParameters] ListShipmentsQuery query, ListShipmentsHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListShipments").Produces<PagedResult<ShipmentSummaryDto>>();

        group.MapPost("/", async (CreateShipmentRequest body, CreateShipmentHandler h, CancellationToken ct) =>
                (await h.HandleAsync(body, ct)).ToCreatedResult(s => $"/api/v1/shipments/{s.Summary.Id}"))
            .WithValidation<CreateShipmentRequest>().WithName("CreateShipment").Produces<ShipmentDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetShipmentHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetShipment").Produces<ShipmentDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/plan", async (Guid id, UpdateShipmentPlanRequest body, EditShipmentHandler h, CancellationToken ct) => (await h.UpdatePlanAsync(id, body, ct)).ToHttpResult())
            .WithValidation<UpdateShipmentPlanRequest>().WithName("UpdateShipmentPlan").Produces<ShipmentDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/orders", async (Guid id, AddOrdersRequest body, EditShipmentHandler h, CancellationToken ct) => (await h.AddOrdersAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AddOrdersRequest>().WithName("AddShipmentOrders").Produces<ShipmentDto>();

        group.MapDelete("/{id:guid}/orders/{orderId:guid}", async (Guid id, Guid orderId, EditShipmentHandler h, CancellationToken ct) => (await h.RemoveOrderAsync(id, orderId, ct)).ToHttpResult())
            .WithName("RemoveShipmentOrder").Produces<ShipmentDto>();

        group.MapPut("/{id:guid}/sequence", async (Guid id, SequenceRequest body, EditShipmentHandler h, CancellationToken ct) => (await h.SequenceAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SequenceRequest>().WithName("SequenceShipmentDrops").Produces<ShipmentDto>();

        group.MapGet("/{id:guid}/quotes", async (Guid id, QuoteShipmentHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("QuoteShipment").Produces<ShipmentQuotesDto>();

        group.MapPost("/{id:guid}/tender", async (Guid id, TenderRequest body, TenderShipmentHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<TenderRequest>().WithName("TenderShipment").Produces<ShipmentDto>().ProducesValidationProblem();

        // Tenders to several transporters: sequential (one after another) or broadcast (all at once, a planner awards a bid).
        group.MapGet("/{id:guid}/tenders", async (Guid id, ListTendersHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("ListShipmentTenders").Produces<IReadOnlyList<TenderDto>>();

        group.MapPost("/{id:guid}/tenders", async (Guid id, StartTenderRequest body, StartTenderHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<StartTenderRequest>().WithName("StartShipmentTender").Produces<TenderDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/tenders/award", async (Guid id, AwardTenderRequest body, TenderDecisionHandler h, CancellationToken ct) => (await h.AwardAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AwardTenderRequest>().WithName("AwardShipmentTender").Produces<TenderDto>();

        group.MapPost("/{id:guid}/tenders/counter-decision", async (Guid id, CounterDecisionRequest body, TenderDecisionHandler h, CancellationToken ct) => (await h.DecideCounterAsync(id, body, ct)).ToHttpResult())
            .WithValidation<CounterDecisionRequest>().WithName("DecideTenderCounterOffer").Produces<TenderDto>();

        group.MapPost("/{id:guid}/tenders/cancel", async (Guid id, ReasonRequest body, TenderDecisionHandler h, CancellationToken ct) => (await h.CancelAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("CancelShipmentTender").Produces<TenderDto>();

        group.MapPost("/{id:guid}/tenders/bid", async (Guid id, BidRequest body, TenderResponseHandler h, CancellationToken ct) => (await h.BidAsync(id, body, ct)).ToHttpResult())
            .WithValidation<BidRequest>().WithName("BidOnShipmentTender").Produces<TenderDto>();

        group.MapPost("/{id:guid}/tenders/counter", async (Guid id, CounterOfferRequest body, TenderResponseHandler h, CancellationToken ct) => (await h.CounterAsync(id, body, ct)).ToHttpResult())
            .WithValidation<CounterOfferRequest>().WithName("CounterShipmentTender").Produces<TenderDto>();

        group.MapPost("/{id:guid}/tenders/decline", async (Guid id, DeclineTenderRequest body, TenderResponseHandler h, CancellationToken ct) => (await h.DeclineAsync(id, body, ct)).ToHttpResult())
            .WithValidation<DeclineTenderRequest>().WithName("DeclineShipmentTender").Produces<TenderDto>();

        group.MapPost("/{id:guid}/withdraw", async (Guid id, ShipmentLifecycleHandler h, CancellationToken ct) => (await h.WithdrawAsync(id, ct)).ToHttpResult())
            .WithName("WithdrawShipment").Produces<ShipmentDto>();

        group.MapGet("/{id:guid}/fleet-options", async (Guid id, FleetOptionsHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("ShipmentFleetOptions").Produces<FleetOptionsDto>();

        group.MapPost("/{id:guid}/accept", async (Guid id, AcceptRequest body, ShipmentLifecycleHandler h, CancellationToken ct) => (await h.AcceptAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AcceptRequest>().WithName("AcceptShipment").Produces<ShipmentDto>();

        group.MapPost("/{id:guid}/reject", async (Guid id, ReasonRequest body, ShipmentLifecycleHandler h, CancellationToken ct) => (await h.RejectAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("RejectShipment").Produces<ShipmentDto>();

        group.MapPost("/{id:guid}/reassign", async (Guid id, AcceptRequest body, ShipmentLifecycleHandler h, CancellationToken ct) => (await h.ReassignAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AcceptRequest>().WithName("ReassignShipment").Produces<ShipmentDto>();

        group.MapPost("/{id:guid}/dispatch", async (Guid id, ShipmentLifecycleHandler h, CancellationToken ct) => (await h.DispatchAsync(id, ct)).ToHttpResult())
            .WithName("DispatchShipment").Produces<ShipmentDto>();

        group.MapPost("/{id:guid}/deliver", async (Guid id, ShipmentLifecycleHandler h, CancellationToken ct) => (await h.DeliverAsync(id, ct)).ToHttpResult())
            .WithName("DeliverShipment").Produces<ShipmentDto>();

        group.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest body, ShipmentLifecycleHandler h, CancellationToken ct) => (await h.CancelAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReasonRequest>().WithName("CancelShipment").Produces<ShipmentDto>();
    }
}
