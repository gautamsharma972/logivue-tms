using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Application.Mobile;
using Tms.Modules.Tracking.Application.Queries;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Tracking.Endpoints;

internal static class TrackingEndpoints
{
    public const string PublicRateLimit = "tracking-public";

    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Tracking").RequireAuthorization();
        MapMobile(api);
        MapShipments(api);
        MapVehicles(api);
        MapControlTower(api);
        MapGeofences(api);
        MapAlertsAndExceptions(api);
        MapLinks(api);
        MapSettings(api);
        MapReports(api);

        // The one anonymous endpoint: a customer opening the link they were given. Rate-limited, read-only and indistinguishable on every failure.
        app.MapGet("/api/v1/public/tracking/{token}", async (string token, CustomerLinkHandler h, CancellationToken ct) => (await h.ViewAsync(token, ct)).ToHttpResult())
            .AllowAnonymous().RequireRateLimiting(PublicRateLimit).WithTags("Tracking").WithName("PublicTracking").Produces<CustomerTrackingDto>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapMobile(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/mobile/tracking");

        g.MapGet("/trips", async (MobileTrackingHandler h, CancellationToken ct) => (await h.TripsAsync(ct)).ToHttpResult())
            .WithName("MobileTrackingTrips").Produces<IReadOnlyList<MobileTripDto>>();

        g.MapGet("/trips/{tripReference}", async (string tripReference, MobileTrackingHandler h, CancellationToken ct) => (await h.CurrentAsync(tripReference, ct)).ToHttpResult())
            .WithName("MobileTrackingSession").Produces<TrackingSessionDto>();

        g.MapPost("/start", async (StartTrackingRequest body, [FromHeader(Name = "Idempotency-Key")] string? key, MobileTrackingHandler h, CancellationToken ct) =>
                (await h.StartAsync(string.IsNullOrWhiteSpace(body.ClientKey) && key is not null ? body with { ClientKey = key } : body, ct)).ToHttpResult())
            .WithValidation<StartTrackingRequest>().WithName("StartTracking").Produces<TrackingSessionDto>();

        g.MapPost("/stop", async (StopTrackingRequest body, [FromHeader(Name = "Idempotency-Key")] string? key, MobileTrackingHandler h, CancellationToken ct) =>
                (await h.StopAsync(string.IsNullOrWhiteSpace(body.ClientKey) && key is not null ? body with { ClientKey = key } : body, ct)).ToHttpResult())
            .WithValidation<StopTrackingRequest>().WithName("StopTracking").Produces<TrackingSessionDto>();

        g.MapPost("/location", async (SingleLocationRequest body, MobileTrackingHandler h, CancellationToken ct) => (await h.LocationAsync(body, ct)).ToHttpResult())
            .WithValidation<SingleLocationRequest>().WithName("SendLocation").Produces<BatchResult>();

        g.MapPost("/location/batch", async (LocationBatch body, MobileTrackingHandler h, CancellationToken ct) => (await h.BatchAsync(body, ct)).ToHttpResult())
            .WithValidation<LocationBatch>().WithName("SendLocationBatch").Produces<BatchResult>();

        g.MapPost("/status", async (DeviceStatusRequest body, MobileTrackingHandler h, CancellationToken ct) => (await h.StatusAsync(body, ct)).ToHttpResult())
            .WithValidation<DeviceStatusRequest>().WithName("ReportDeviceStatus").Produces(StatusCodes.Status204NoContent);

        g.MapPost("/sync", async (SyncRequest body, MobileTrackingHandler h, CancellationToken ct) => (await h.SyncAsync(body, ct)).ToHttpResult())
            .WithName("SyncTracking").Produces<IReadOnlyList<SyncCommandResult>>();
    }

    private static void MapShipments(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tracking/shipments");

        g.MapGet("/", async ([AsParameters] ListShipmentsQuery query, ShipmentQueryHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult())
            .WithName("ListTrackedShipments").Produces<PagedResult<TrackedShipmentSummaryDto>>();

        g.MapGet("/{id:guid}", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .WithName("GetTrackedShipment").Produces<TrackedShipmentDto>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapGet("/{id:guid}/current-location", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.CurrentLocationAsync(id, ct)).ToHttpResult())
            .WithName("GetCurrentLocation").Produces<CurrentLocationDto>();

        g.MapGet("/{id:guid}/timeline", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.TimelineAsync(id, ct)).ToHttpResult())
            .WithName("GetTrackingTimeline").Produces<IReadOnlyList<TimelineEntryDto>>();

        g.MapGet("/{id:guid}/eta", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.EtaAsync(id, ct)).ToHttpResult())
            .WithName("GetShipmentEta").Produces<EtaDto>();

        g.MapGet("/{id:guid}/route", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.RouteAsync(id, ct)).ToHttpResult())
            .WithName("GetShipmentRoute").Produces<RouteDto>();

        g.MapGet("/{id:guid}/locations", async (Guid id, [AsParameters] LocationsQuery query, ShipmentQueryHandler h, CancellationToken ct) => (await h.LocationsAsync(id, query, ct)).ToHttpResult())
            .WithName("GetShipmentLocations").Produces<PagedResult<LocationDto>>();

        g.MapGet("/{id:guid}/exceptions", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.ExceptionsAsync(id, ct)).ToHttpResult())
            .WithName("GetShipmentTrackingExceptions").Produces<IReadOnlyList<TrackingExceptionSummaryDto>>();

        g.MapGet("/{id:guid}/health", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.HealthAsync(id, ct)).ToHttpResult())
            .WithName("GetShipmentTrackingHealth").Produces<TrackingHealthDto>();

        g.MapGet("/{id:guid}/analytics", async (Guid id, ShipmentQueryHandler h, CancellationToken ct) => (await h.AnalyticsAsync(id, ct)).ToHttpResult())
            .WithName("GetShipmentJourneyAnalytics").Produces<JourneyAnalyticsDto>();

        g.MapGet("/{id:guid}/evidence", async (Guid id, ShipmentQueryHandler h, Tms.SharedKernel.Contracts.ITrackingClaimsIntegration claims, CancellationToken ct) =>
            {
                var found = await h.FindAsync(id, includeStops: false, ct);
                return found.IsFailure ? found.Error.ToProblem() : Results.Ok(await claims.GetEvidenceAsync(found.Value.TripReference, ct));
            })
            .WithName("GetShipmentTrackingEvidence").Produces<Tms.SharedKernel.Contracts.TrackingEvidence>();

        g.MapPost("/{id:guid}/eta/override", async (Guid id, OverrideEtaRequest body, OverrideHandler h, CancellationToken ct) => (await h.OverrideEtaAsync(id, body, ct)).ToHttpResult())
            .WithValidation<OverrideEtaRequest>().WithName("OverrideShipmentEta").Produces<EtaDto>();

        g.MapDelete("/{id:guid}/eta/override", async (Guid id, OverrideHandler h, CancellationToken ct) => (await h.ClearEtaOverrideAsync(id, ct)).ToHttpResult())
            .WithName("ClearShipmentEtaOverride").Produces<EtaDto>();

        g.MapPost("/{id:guid}/milestones", async (Guid id, ManualMilestoneRequest body, OverrideHandler h, CancellationToken ct) => (await h.ManualMilestoneAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ManualMilestoneRequest>().WithName("RecordManualMilestone").Produces<TimelineEntryDto>();

        g.MapPost("/{id:guid}/delay-reason", async (Guid id, SetDelayReasonRequest body, OverrideHandler h, CancellationToken ct) => (await h.SetDelayReasonAsync(id, body, ct)).ToHttpResult())
            .WithName("SetShipmentDelayReason").Produces<TrackedShipmentDto>();

        api.MapPost("/tracking/deviations/{id:guid}/reason", async (Guid id, DeviationReasonRequest body, OverrideHandler h, CancellationToken ct) => (await h.DeviationReasonAsync(id, body, ct)).ToHttpResult())
            .WithName("RecordDeviationReason").Produces<RouteDeviationDto>();

        api.MapPost("/tracking/eta/recalculate", async (RecalculateEtaRequest body, OverrideHandler h, CancellationToken ct) => (await h.RecalculateAsync(body, ct)).ToHttpResult())
            .WithName("RecalculateEta").Produces<EtaDto>();
    }

    private static void MapVehicles(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tracking/vehicles");

        g.MapGet("/", async (string? search, TrackingHealth? health, int? page, int? pageSize, VehicleQueryHandler h, CancellationToken ct) => (await h.ListAsync(search, health, page ?? 1, pageSize ?? 50, ct)).ToHttpResult())
            .WithName("ListTrackedVehicles").Produces<PagedResult<VehicleTrackingDto>>();

        g.MapGet("/{vehicle}/current", async (string vehicle, VehicleQueryHandler h, CancellationToken ct) => (await h.CurrentAsync(vehicle, ct)).ToHttpResult())
            .WithName("GetVehicleCurrent").Produces<VehicleTrackingDto>();

        g.MapGet("/{vehicle}/history", async (string vehicle, DateOnly? day, int? maxPoints, VehicleQueryHandler h, CancellationToken ct) => (await h.HistoryAsync(vehicle, day, maxPoints, ct)).ToHttpResult())
            .WithName("GetVehicleHistory").Produces<IReadOnlyList<LocationDto>>();
    }

    private static void MapControlTower(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/control-tower");

        g.MapGet("/summary", async (Guid? transporterId, ControlTowerHandler h, CancellationToken ct) => (await h.SummaryAsync(transporterId, ct)).ToHttpResult())
            .WithName("ControlTowerSummary").Produces<ControlTowerSummaryDto>();

        g.MapGet("/shipments", async ([AsParameters] ListShipmentsQuery query, ControlTowerHandler h, CancellationToken ct) => (await h.ShipmentsAsync(query, ct)).ToHttpResult())
            .WithName("ControlTowerShipments").Produces<PagedResult<TrackedShipmentSummaryDto>>();

        g.MapGet("/map", async (ControlTowerHandler h, CancellationToken ct) => (await h.MapAsync(ct)).ToHttpResult())
            .WithName("ControlTowerMap").Produces<IReadOnlyList<TrackedShipmentSummaryDto>>();

        g.MapGet("/exceptions", async ([AsParameters] ListExceptionsQuery query, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult())
            .WithName("ControlTowerExceptions").Produces<PagedResult<TrackingExceptionSummaryDto>>();

        g.MapGet("/vehicles", async (string? search, TrackingHealth? health, int? page, int? pageSize, VehicleQueryHandler h, CancellationToken ct) => (await h.ListAsync(search, health, page ?? 1, pageSize ?? 100, ct)).ToHttpResult())
            .WithName("ControlTowerVehicles").Produces<PagedResult<VehicleTrackingDto>>();
    }

    private static void MapGeofences(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tracking/geofences");

        g.MapGet("/", async (GeofenceType? type, GeofenceStatus? status, GeofenceHandler h, CancellationToken ct) => (await h.ListAsync(type, status, ct)).ToHttpResult())
            .WithName("ListGeofences").Produces<IReadOnlyList<GeofenceDto>>();

        g.MapPost("/", async (SaveGeofenceRequest body, GeofenceHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToCreatedResult(x => $"/api/v1/tracking/geofences/{x.Id}"))
            .WithValidation<SaveGeofenceRequest>().WithName("CreateGeofence").Produces<GeofenceDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        g.MapPut("/{id:guid}", async (Guid id, SaveGeofenceRequest body, GeofenceHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveGeofenceRequest>().WithName("UpdateGeofence").Produces<GeofenceDto>().ProducesValidationProblem();

        g.MapDelete("/{id:guid}", async (Guid id, GeofenceHandler h, CancellationToken ct) => (await h.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteGeofence").Produces(StatusCodes.Status204NoContent);
    }

    private static void MapAlertsAndExceptions(RouteGroupBuilder api)
    {
        var alerts = api.MapGroup("/tracking/alerts");
        alerts.MapGet("/", async ([AsParameters] ListAlertsQuery query, AlertHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult()).WithName("ListTrackingAlerts").Produces<PagedResult<AlertDto>>();
        alerts.MapPost("/{id:guid}/acknowledge", async (Guid id, AlertHandler h, CancellationToken ct) => (await h.AcknowledgeAsync(id, ct)).ToHttpResult()).WithName("AcknowledgeTrackingAlert").Produces<AlertDto>();
        alerts.MapPost("/{id:guid}/resolve", async (Guid id, ResolveAlertRequest? body, AlertHandler h, CancellationToken ct) => (await h.ResolveAsync(id, body ?? new ResolveAlertRequest(null), ct)).ToHttpResult())
            .WithName("ResolveTrackingAlert").Produces<AlertDto>();

        var ex = api.MapGroup("/tracking/exceptions");
        ex.MapGet("/", async ([AsParameters] ListExceptionsQuery query, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult())
            .WithName("ListTrackingExceptions").Produces<PagedResult<TrackingExceptionSummaryDto>>();
        ex.MapGet("/{id:guid}", async (Guid id, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult()).WithName("GetTrackingException").Produces<TrackingExceptionDto>();
        ex.MapPost("/{id:guid}/acknowledge", async (Guid id, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.AcknowledgeAsync(id, ct)).ToHttpResult()).WithName("AcknowledgeTrackingException").Produces<TrackingExceptionDto>();
        ex.MapPost("/{id:guid}/assign", async (Guid id, AssignExceptionRequest body, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.AssignAsync(id, body, ct)).ToHttpResult())
            .WithName("AssignTrackingException").Produces<TrackingExceptionDto>();
        ex.MapPost("/{id:guid}/escalate", async (Guid id, EscalateExceptionRequest body, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.EscalateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<EscalateExceptionRequest>().WithName("EscalateTrackingException").Produces<TrackingExceptionDto>();
        ex.MapPost("/{id:guid}/resolve", async (Guid id, ResolveExceptionRequest body, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.ResolveAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ResolveExceptionRequest>().WithName("ResolveTrackingException").Produces<TrackingExceptionDto>();
        ex.MapPost("/{id:guid}/close", async (Guid id, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.CloseAsync(id, ct)).ToHttpResult()).WithName("CloseTrackingException").Produces<TrackingExceptionDto>();
        ex.MapPost("/{id:guid}/notes", async (Guid id, NoteRequest body, Application.Queries.ExceptionHandler h, CancellationToken ct) => (await h.NoteAsync(id, body, ct)).ToHttpResult())
            .WithValidation<NoteRequest>().WithName("NoteTrackingException").Produces<TrackingExceptionDto>();
    }

    private static void MapLinks(RouteGroupBuilder api)
    {
        api.MapPost("/tracking/links", async (CreateLinkRequest body, CustomerLinkHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToCreatedResult(x => $"/api/v1/tracking/links/{x.Link.Id}"))
            .WithValidation<CreateLinkRequest>().WithName("CreateCustomerTrackingLink").Produces<CreatedLinkDto>(StatusCodes.Status201Created);

        api.MapGet("/tracking/shipments/{id:guid}/links", async (Guid id, CustomerLinkHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListCustomerTrackingLinks").Produces<IReadOnlyList<CustomerLinkDto>>();

        api.MapPost("/tracking/links/{id:guid}/revoke", async (Guid id, CustomerLinkHandler h, CancellationToken ct) => (await h.RevokeAsync(id, ct)).ToHttpResult())
            .WithName("RevokeCustomerTrackingLink").Produces<CustomerLinkDto>();
    }

    private static void MapSettings(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/tracking/settings");
        g.MapGet("/", async (Application.Queries.SettingsHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult()).WithName("ListTrackingSettings").Produces<IReadOnlyList<Application.Queries.SettingDto>>();
        g.MapPut("/{key}", async (string key, JsonElement body, Application.Queries.SettingsHandler h, CancellationToken ct) => (await h.SaveAsync(key, body, ct)).ToHttpResult())
            .WithName("SaveTrackingSetting").Produces<Application.Queries.SettingDto>().ProducesValidationProblem();
    }

    private static void MapReports(RouteGroupBuilder api) =>
        api.MapGet("/tracking/reports/{report}", async (string report, [AsParameters] TrackingReportQuery query, ReportsHandler h, CancellationToken ct) =>
            {
                var result = await h.RunAsync(report, query, ct);
                return result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("TrackingReport").Produces(StatusCodes.Status200OK).ProducesValidationProblem();
}
