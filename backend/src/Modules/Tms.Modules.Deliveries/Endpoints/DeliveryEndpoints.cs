using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Application.Deliveries;
using Tms.Modules.Deliveries.Application.Claims;
using Tms.Modules.Deliveries.Application.Dashboard;
using Tms.Modules.Deliveries.Application.Exceptions;
using Tms.Modules.Deliveries.Application.Notifications;
using Tms.Modules.Deliveries.Application.Execution;
using Tms.Modules.Deliveries.Application.Mobile;
using Tms.Modules.Deliveries.Application.Pods;
using Tms.Modules.Deliveries.Application.Settings;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Deliveries.Endpoints;

/// <summary>All routes require sign-in; permissions and the vendor scoping are enforced inside the handlers (<see cref="DeliveryAccess"/>).</summary>
internal static class DeliveryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Deliveries").RequireAuthorization();
        MapDeliveries(api);
        MapPods(api);
        MapExceptions(api);
        MapMobile(api);
        MapSettings(api);
        MapDashboard(api);
    }

    private static void MapDeliveries(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/deliveries");

        group.MapGet("/", async ([AsParameters] ListDeliveriesQuery query, ListDeliveriesHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListDeliveries").Produces<PagedResult<DeliverySummaryDto>>();

        group.MapPost("/", async (SaveDeliveryRequest body, SaveDeliveryHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToCreatedResult(d => $"/api/v1/deliveries/{d.Summary.Id}"))
            .WithValidation<SaveDeliveryRequest>().WithName("CreateDelivery").Produces<DeliveryDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetDeliveryHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetDelivery").Produces<DeliveryDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, SaveDeliveryRequest body, SaveDeliveryHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveDeliveryRequest>().WithName("UpdateDelivery").Produces<DeliveryDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignDeliveryRequest body, AssignDeliveryHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AssignDeliveryRequest>().WithName("AssignDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/start", async (Guid id, DeviceContext? body, ExecutionHandler h, CancellationToken ct) => (await h.StartAsync(id, body, ct)).ToHttpResult())
            .WithName("StartDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/arrive", async (Guid id, DeviceContext? body, ExecutionHandler h, CancellationToken ct) => (await h.ArriveAsync(id, body, ct)).ToHttpResult())
            .WithName("ArriveAtDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/otp/issue", async (Guid id, DeviceContext? body, ExecutionHandler h, CancellationToken ct) => (await h.IssueOtpAsync(id, body, ct)).ToHttpResult())
            .WithName("IssueDeliveryOtp").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/otp/verify", async (Guid id, VerifyOtpRequest body, ExecutionHandler h, CancellationToken ct) => (await h.VerifyOtpAsync(id, body, ct)).ToHttpResult())
            .WithValidation<VerifyOtpRequest>().WithName("VerifyDeliveryOtp").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/attempt", async (Guid id, AttemptRequest body, ExecutionHandler h, CancellationToken ct) => (await h.AttemptAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AttemptRequest>().WithName("AttemptDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/complete", async (Guid id, CompleteDeliveryRequest body, ExecutionHandler h, CancellationToken ct) => (await h.CompleteAsync(id, body, ct)).ToHttpResult())
            .WithValidation<CompleteDeliveryRequest>().WithName("CompleteDelivery").Produces<DeliveryDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/fail", async (Guid id, FailDeliveryRequest body, ExecutionHandler h, CancellationToken ct) => (await h.FailAsync(id, body, ct)).ToHttpResult())
            .WithValidation<FailDeliveryRequest>().WithName("FailDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/refuse", async (Guid id, RefuseDeliveryRequest body, ExecutionHandler h, CancellationToken ct) => (await h.RefuseAsync(id, body, ct)).ToHttpResult())
            .WithValidation<RefuseDeliveryRequest>().WithName("RefuseDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleRequest body, ExecutionHandler h, CancellationToken ct) => (await h.RescheduleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<RescheduleRequest>().WithName("RescheduleDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/cancel", async (Guid id, Application.ReasonRequest body, ExecutionHandler h, CancellationToken ct) => (await h.CancelAsync(id, body, ct)).ToHttpResult())
            .WithValidation<Application.ReasonRequest>().WithName("CancelDelivery").Produces<DeliveryDto>();

        group.MapPost("/{id:guid}/close", async (Guid id, Application.ReasonRequest body, ExecutionHandler h, CancellationToken ct) => (await h.CloseAsync(id, body, ct)).ToHttpResult())
            .WithValidation<Application.ReasonRequest>().WithName("CloseDelivery").Produces<DeliveryDto>();

        group.MapGet("/{id:guid}/items", async (Guid id, DeliveryItemsHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListDeliveryItems").Produces<IReadOnlyList<DeliveryItemDto>>();

        group.MapPost("/{id:guid}/items/reconcile", async (Guid id, ReconcileItemsRequest body, DeliveryItemsHandler h, CancellationToken ct) => (await h.ReconcileAsync(id, body, ct)).ToHttpResult())
            .WithName("ReconcileDeliveryItems").Produces<IReadOnlyList<ReconciliationDto>>();

        group.MapPost("/{id:guid}/claims", async (Guid id, CreateClaimsRequest body, ClaimHandler h, CancellationToken ct) => (await h.CreateAsync(id, body, ct)).ToHttpResult())
            .WithName("CreateDeliveryClaims").Produces<IReadOnlyList<ClaimResultDto>>();

        group.MapGet("/{id:guid}/billing", async (Guid id, ClaimHandler h, CancellationToken ct) => (await h.BillingAsync(id, ct)).ToHttpResult())
            .WithName("GetDeliveryBilling").Produces<BillingStatusDto>();

        group.MapPost("/{id:guid}/pod", async (Guid id, PodHandler h, CancellationToken ct) => (await h.CreateAsync(id, ct)).ToHttpResult())
            .WithName("StartDeliveryPod").Produces<PodDto>();
    }

    private static void MapPods(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/pods");

        group.MapGet("/", async ([AsParameters] ListPodsQuery query, ListPodsHandler h, CancellationToken ct) => (await h.HandleAsync(query, reviewQueue: false, ct)).ToHttpResult())
            .WithName("ListPods").Produces<PagedResult<PodSummaryDto>>();

        group.MapPost("/", async (CreatePodRequest body, PodHandler h, CancellationToken ct) => (await h.CreateAsync(body.DeliveryId, ct)).ToHttpResult())
            .WithName("CreatePod").Produces<PodDto>();

        group.MapGet("/review-queue", async ([AsParameters] ListPodsQuery query, ListPodsHandler h, CancellationToken ct) => (await h.HandleAsync(query, reviewQueue: true, ct)).ToHttpResult())
            .WithName("PodReviewQueue").Produces<PagedResult<PodSummaryDto>>();

        group.MapGet("/{id:guid}", async (Guid id, PodHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .WithName("GetPod").Produces<PodDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/review", async (Guid id, PodHandler h, CancellationToken ct) => (await h.ReviewAsync(id, ct)).ToHttpResult())
            .WithName("GetPodForReview").Produces<PodReviewDto>();

        group.MapPut("/{id:guid}/proof", async (Guid id, UpdateProofRequest body, PodHandler h, CancellationToken ct) => (await h.UpdateProofAsync(id, body, ct)).ToHttpResult())
            .WithValidation<UpdateProofRequest>().WithName("UpdatePodProof").Produces<PodDto>();

        group.MapPost("/{id:guid}/submit", async (Guid id, PodHandler h, CancellationToken ct) => (await h.SubmitAsync(id, ct)).ToHttpResult())
            .WithName("SubmitPod").Produces<PodDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/resubmit", async (Guid id, PodHandler h, CancellationToken ct) => (await h.SubmitAsync(id, ct)).ToHttpResult())
            .WithName("ResubmitPod").Produces<PodDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/validate", async (Guid id, PodHandler h, CancellationToken ct) => (await h.ValidateAsync(id, ct)).ToHttpResult())
            .WithName("ValidatePod").Produces<PodDto>();

        group.MapPost("/{id:guid}/review", async (Guid id, ReviewPodRequest body, PodHandler h, CancellationToken ct) => (await h.DecideAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReviewPodRequest>().WithName("ReviewPod").Produces<PodDto>();

        group.MapPost("/{id:guid}/approve", async (Guid id, PodHandler h, CancellationToken ct) => (await h.DecideAsync(id, new ReviewPodRequest(ReviewActions.Accept, null), ct)).ToHttpResult())
            .WithName("ApprovePod").Produces<PodDto>();

        group.MapPost("/{id:guid}/reject", async (Guid id, Application.ReasonRequest body, PodHandler h, CancellationToken ct) => (await h.DecideAsync(id, new ReviewPodRequest(ReviewActions.Reject, body.Reason), ct)).ToHttpResult())
            .WithValidation<Application.ReasonRequest>().WithName("RejectProofOfDelivery").Produces<PodDto>();

        group.MapPost("/{id:guid}/correction", async (Guid id, RequestCorrectionRequest body, PodHandler h, CancellationToken ct) => (await h.RequestCorrectionAsync(id, body, ct)).ToHttpResult())
            .WithValidation<RequestCorrectionRequest>().WithName("RequestPodCorrection").Produces<PodDto>();

        group.MapGet("/{id:guid}/evidence", async (Guid id, PodHandler h, CancellationToken ct) => (await h.EvidenceAsync(id, ct)).ToHttpResult())
            .WithName("ListPodEvidence").Produces<IReadOnlyList<EvidenceDto>>();

        group.MapPost("/{id:guid}/evidence", async (Guid id, [FromForm] UploadEvidenceForm form, [FromHeader(Name = "Idempotency-Key")] string? key, PodHandler h, CancellationToken ct) =>
                (await h.AddEvidenceAsync(id, form, key, ct)).ToCreatedResult(e => $"/api/v1/pods/{id}/evidence"))
            .DisableAntiforgery() // bearer-token API: there is no cookie session for a CSRF attack to ride on
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024))
            .WithName("AddPodEvidence").Accepts<UploadEvidenceForm>("multipart/form-data").Produces<EvidenceDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapDelete("/{id:guid}/evidence/{evidenceId:guid}", async (Guid id, Guid evidenceId, [FromQuery] string reason, PodHandler h, CancellationToken ct) => (await h.RemoveEvidenceAsync(id, evidenceId, reason, ct)).ToHttpResult())
            .WithName("RemovePodEvidence").Produces<PodDto>();

        group.MapPost("/{id:guid}/signature", async (Guid id, [FromForm] UploadSignatureForm form, PodHandler h, CancellationToken ct) =>
                (await h.AddSignatureAsync(id, form, ct)).ToCreatedResult(s => $"/api/v1/pods/{id}"))
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(2 * 1024 * 1024))
            .WithName("AddPodSignature").Accepts<UploadSignatureForm>("multipart/form-data").Produces<SignatureDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapPost("/{id:guid}/ocr", async (Guid id, PodHandler h, CancellationToken ct) => (await h.QueueOcrAsync(id, ct)).ToHttpResult())
            .WithName("QueuePodOcr").Produces<OcrResultDto>();

        group.MapGet("/{id:guid}/ocr", async (Guid id, PodHandler h, CancellationToken ct) => (await h.OcrAsync(id, ct)).ToHttpResult())
            .WithName("GetPodOcr").Produces<IReadOnlyList<OcrResultDto>>();

        group.MapPost("/{id:guid}/ocr/review", async (Guid id, ReviewOcrFieldRequest body, PodHandler h, CancellationToken ct) => (await h.ReviewOcrFieldAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ReviewOcrFieldRequest>().WithName("ReviewPodOcrField").Produces<PodDto>();

        api.MapGet("/pod-evidence/{evidenceId:guid}/file", async (Guid evidenceId, PodHandler h, CancellationToken ct) =>
            {
                var result = await h.OpenEvidenceAsync(evidenceId, ct);
                return result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("DownloadPodEvidence").Produces(StatusCodes.Status200OK);

        api.MapGet("/pod-signatures/{signatureId:guid}/file", async (Guid signatureId, PodHandler h, CancellationToken ct) =>
            {
                var result = await h.OpenSignatureAsync(signatureId, ct);
                return result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("DownloadPodSignature").Produces(StatusCodes.Status200OK);
    }

    private static void MapExceptions(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/delivery-exceptions");

        group.MapGet("/", async ([AsParameters] ListExceptionsQuery query, ExceptionHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult())
            .WithName("ListDeliveryExceptions").Produces<PagedResult<ExceptionSummaryDto>>();

        group.MapPost("/", async (RaiseExceptionRequest body, ExceptionHandler h, CancellationToken ct) => (await h.RaiseAsync(body, ct)).ToCreatedResult(e => $"/api/v1/delivery-exceptions/{e.Summary.Id}"))
            .WithValidation<RaiseExceptionRequest>().WithName("RaiseDeliveryException").Produces<ExceptionDto>(StatusCodes.Status201Created);

        group.MapGet("/{id:guid}", async (Guid id, ExceptionHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .WithName("GetDeliveryException").Produces<ExceptionDto>();

        group.MapPost("/{id:guid}/acknowledge", async (Guid id, ExceptionHandler h, CancellationToken ct) => (await h.AcknowledgeAsync(id, ct)).ToHttpResult())
            .WithName("AcknowledgeDeliveryException").Produces<ExceptionDto>();

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignExceptionRequest body, ExceptionHandler h, CancellationToken ct) => (await h.AssignAsync(id, body, ct)).ToHttpResult())
            .WithValidation<AssignExceptionRequest>().WithName("AssignDeliveryException").Produces<ExceptionDto>();

        group.MapPost("/{id:guid}/investigate", async (Guid id, InvestigateExceptionRequest body, ExceptionHandler h, CancellationToken ct) => (await h.InvestigateAsync(id, body, ct)).ToHttpResult())
            .WithName("InvestigateDeliveryException").Produces<ExceptionDto>();

        group.MapPost("/{id:guid}/escalate", async (Guid id, Application.ReasonRequest body, ExceptionHandler h, CancellationToken ct) => (await h.EscalateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<Application.ReasonRequest>().WithName("EscalateDeliveryException").Produces<ExceptionDto>();

        group.MapPost("/{id:guid}/attachments", async (Guid id, [FromForm] AttachToExceptionForm form, ExceptionHandler h, CancellationToken ct) => (await h.AttachAsync(id, form, ct)).ToHttpResult())
            .DisableAntiforgery() // bearer-token API: there is no cookie session for a CSRF attack to ride on
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024))
            .WithName("AttachToDeliveryException").Accepts<AttachToExceptionForm>("multipart/form-data").Produces<ExceptionDto>().ProducesValidationProblem();

        api.MapGet("/exception-attachments/{attachmentId:guid}/file", async (Guid attachmentId, ExceptionHandler h, CancellationToken ct) =>
            {
                var result = await h.OpenAttachmentAsync(attachmentId, ct);
                return result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("DownloadExceptionAttachment").Produces(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/notes", async (Guid id, NoteRequest body, ExceptionHandler h, CancellationToken ct) => (await h.NoteAsync(id, body, ct)).ToHttpResult())
            .WithValidation<NoteRequest>().WithName("AddDeliveryExceptionNote").Produces<ExceptionDto>();

        group.MapPost("/{id:guid}/resolve", async (Guid id, ResolveExceptionRequest body, ExceptionHandler h, CancellationToken ct) => (await h.ResolveAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ResolveExceptionRequest>().WithName("ResolveDeliveryException").Produces<ExceptionDto>();

        group.MapPost("/{id:guid}/close", async (Guid id, ExceptionHandler h, CancellationToken ct) => (await h.CloseAsync(id, ct)).ToHttpResult())
            .WithName("CloseDeliveryException").Produces<ExceptionDto>();
    }

    private static void MapMobile(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/mobile");

        group.MapGet("/deliveries", async ([FromQuery] Guid? transporterId, MobileHandler h, CancellationToken ct) => (await h.DownloadAsync(transporterId, ct)).ToHttpResult())
            .WithName("MobileDeliveries").Produces<MobileBundleDto>();

        group.MapPost("/sync", async (MobileSyncRequest body, MobileHandler h, CancellationToken ct) => (await h.SyncAsync(body, ct)).ToHttpResult())
            .WithValidation<MobileSyncRequest>().WithName("MobileSync").Produces<MobileSyncResponse>().ProducesValidationProblem();
    }

    private static void MapSettings(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/delivery-settings");

        group.MapGet("/", async (SettingsHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult())
            .WithName("ListDeliverySettings").Produces<IReadOnlyList<SettingDto>>();

        group.MapPut("/{key}", async (string key, JsonElement body, SettingsHandler h, CancellationToken ct) => (await h.SaveAsync(key, body, ct)).ToHttpResult())
            .WithName("SaveDeliverySetting").Produces<SettingDto>().ProducesValidationProblem();
    }

    private static void MapDashboard(RouteGroupBuilder api)
    {
        var board = api.MapGroup("/pod-dashboard");

        board.MapGet("/summary", async ([AsParameters] DashboardQuery query, DashboardHandler h, CancellationToken ct) => (await h.SummaryAsync(query, ct)).ToHttpResult())
            .WithName("PodDashboardSummary").Produces<DashboardSummaryDto>();

        board.MapGet("/ageing", async ([FromQuery] Guid? transporterId, DashboardHandler h, CancellationToken ct) => (await h.AgeingAsync(transporterId, ct)).ToHttpResult())
            .WithName("PodDashboardAgeing").Produces<AgeingDto>();

        board.MapGet("/ageing/items", async ([AsParameters] AgeingItemsQuery query, DashboardHandler h, CancellationToken ct) => (await h.AgeingItemsAsync(query, ct)).ToHttpResult())
            .WithName("PodDashboardAgeingItems").Produces<PagedResult<AgeingItemDto>>();

        board.MapGet("/compliance", async ([AsParameters] ComplianceQuery query, DashboardHandler h, CancellationToken ct) => (await h.ComplianceAsync(query, ct)).ToHttpResult())
            .WithName("PodDashboardCompliance").Produces<ComplianceDto>();

        board.MapGet("/exceptions", async ([FromQuery] Guid? transporterId, DashboardHandler h, CancellationToken ct) => (await h.ExceptionsAsync(transporterId, ct)).ToHttpResult())
            .WithName("PodDashboardExceptions").Produces<ExceptionsSummaryDto>();

        api.MapGet("/delivery-reports/{report}", async (string report, [AsParameters] ReportQuery query, ReportsHandler h, CancellationToken ct) =>
            {
                var result = await h.RunAsync(report, query, ct);
                return result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("DeliveryReport").Produces(StatusCodes.Status200OK).ProducesValidationProblem();

        var notices = api.MapGroup("/delivery-notifications");
        notices.MapGet("/", async ([AsParameters] ListNotificationsQuery query, NotificationHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult())
            .WithName("ListDeliveryNotifications").Produces<PagedResult<NotificationDto>>();
        notices.MapPost("/read-all", async (NotificationHandler h, CancellationToken ct) => (await h.MarkReadAsync(null, ct)).ToHttpResult())
            .WithName("ReadAllDeliveryNotifications").Produces(StatusCodes.Status204NoContent);
        notices.MapPost("/{id:guid}/read", async (Guid id, NotificationHandler h, CancellationToken ct) => (await h.MarkReadAsync(id, ct)).ToHttpResult())
            .WithName("ReadDeliveryNotification").Produces(StatusCodes.Status204NoContent);

        api.MapGet("/delivery-reliability", async ([FromQuery] string origin, [FromQuery] string destination, [FromQuery] int? days, DeliveryAccess access, IDeliveryReliabilityFeed feed, CancellationToken ct) =>
                access.CanRead ? Results.Ok(await feed.GetLaneReliabilityAsync(origin, destination, days ?? 90, ct)) : DeliveryAccess.Forbidden.ToProblem())
            .WithName("DeliveryLaneReliability").Produces<LaneReliability>();
    }
}
