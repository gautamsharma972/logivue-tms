using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.TransportOps.Application;
using Tms.Modules.TransportOps.Domain;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.TransportOps.Endpoints;

internal static class TransportOpsEndpoints
{
    public const string PublicRateLimit = "ops-public";

    public static void Map(IEndpointRouteBuilder app)
    {
        MapPublic(app);
        var api = app.MapGroup("/api/v1/ops").WithTags("Transporter operations").RequireAuthorization();
        MapMasters(api);
        MapRequests(api);
        MapLrs(api);
        MapTrips(api);
        MapPods(api);
        MapBilling(api);
        MapInvoices(api);
        MapSettings(api);
    }

    // ---------------------------------------------------------------- the driver's QR page: the only anonymous endpoints
    private static void MapPublic(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/public/ops/lr").AllowAnonymous().RequireRateLimiting(PublicRateLimit).WithTags("Transporter operations (QR)");

        g.MapGet("/{token}", async (string token, QrHandler h, CancellationToken ct) => (await h.ViewAsync(token, ct)).ToHttpResult())
            .WithName("QrView").Produces<QrViewDto>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/{token}/arrival", async (string token, ArrivalRequest body, [FromHeader(Name = "Idempotency-Key")] string? key, QrHandler h, CancellationToken ct) =>
                (await h.ArrivalAsync(token, string.IsNullOrWhiteSpace(body.ClientKey) && key is not null ? body with { ClientKey = key } : body, ct)).ToHttpResult())
            .WithValidation<ArrivalRequest>().WithName("QrArrival").Produces<QrViewDto>();

        g.MapPost("/{token}/unloading", async (string token, UnloadingRequest body, [FromHeader(Name = "Idempotency-Key")] string? key, QrHandler h, CancellationToken ct) =>
                (await h.UnloadingAsync(token, string.IsNullOrWhiteSpace(body.ClientKey) && key is not null ? body with { ClientKey = key } : body, ct)).ToHttpResult())
            .WithName("QrUnloading").Produces<QrViewDto>();

        g.MapPost("/{token}/pod-pending", async (string token, PodPendingRequest body, [FromHeader(Name = "Idempotency-Key")] string? key, QrHandler h, CancellationToken ct) =>
                (await h.PodPendingAsync(token, string.IsNullOrWhiteSpace(body.ClientKey) && key is not null ? body with { ClientKey = key } : body, ct)).ToHttpResult())
            .WithName("QrPodPending").Produces<QrViewDto>();

        g.MapPost("/{token}/pod", async (string token, HttpRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, QrHandler h, CancellationToken ct) =>
            {
                var uploads = await ReadFilesAsync(request, ct);
                return (await h.UploadPodAsync(token, uploads, key, ct)).ToHttpResult();
            })
            .DisableAntiforgery() // no cookie session exists on this page for a CSRF attack to ride on
            .WithName("QrUploadPod").Produces<QrViewDto>();
    }

    private static async Task<IReadOnlyList<UploadedFile>> ReadFilesAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return [];
        }

        try
        {
            var form = await request.ReadFormAsync(ct);
            return form.Files.Select(f => new UploadedFile(f.FileName, f.OpenReadStream())).ToList();
        }
        catch (Exception e) when (e is InvalidDataException or IOException or BadHttpRequestException)
        {
            // A broken or truncated upload is the caller's mistake (400), not a server fault.
            return [];
        }
    }

    // ---------------------------------------------------------------- customers, locations, agreements
    private static void MapMasters(RouteGroupBuilder api)
    {
        api.MapGet("/customers", async (string? search, ActiveStatus? status, int? page, int? pageSize, CustomerHandler h, CancellationToken ct) =>
                (await h.ListAsync(search, status, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsCustomers").Produces<PagedResult<CustomerDto>>();
        api.MapGet("/customers/{id:guid}", async (Guid id, CustomerHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsCustomer").Produces<CustomerDto>();
        api.MapPost("/customers", async (SaveCustomerRequest body, CustomerHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Masters).WithValidation<SaveCustomerRequest>().WithName("OpsCreateCustomer").Produces<CustomerDto>();
        api.MapPut("/customers/{id:guid}", async (Guid id, SaveCustomerRequest body, CustomerHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Masters).WithValidation<SaveCustomerRequest>().WithName("OpsUpdateCustomer").Produces<CustomerDto>();

        api.MapGet("/uoms", async (bool? activeOnly, UomHandler h, CancellationToken ct) => (await h.ListAsync(activeOnly ?? true, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsUoms").Produces<IReadOnlyList<UomDto>>();
        api.MapPost("/uoms", async (SaveUomRequest body, UomHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Masters).WithValidation<SaveUomRequest>().WithName("OpsCreateUom").Produces<UomDto>();
        api.MapPut("/uoms/{id:guid}", async (Guid id, SaveUomRequest body, UomHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Masters).WithValidation<SaveUomRequest>().WithName("OpsUpdateUom").Produces<UomDto>();

        api.MapGet("/locations", async (string? search, LocationKind? kind, Guid? customerId, ActiveStatus? status, int? page, int? pageSize, LocationHandler h, CancellationToken ct) =>
                (await h.ListAsync(search, kind, customerId, status, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsLocations").Produces<PagedResult<LocationDto>>();
        api.MapGet("/locations/{id:guid}", async (Guid id, LocationHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsLocation").Produces<LocationDto>();
        api.MapPost("/locations", async (SaveLocationRequest body, LocationHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Masters).WithValidation<SaveLocationRequest>().WithName("OpsCreateLocation").Produces<LocationDto>();
        api.MapPut("/locations/{id:guid}", async (Guid id, SaveLocationRequest body, LocationHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Masters).WithValidation<SaveLocationRequest>().WithName("OpsUpdateLocation").Produces<LocationDto>();

        api.MapGet("/agreements", async (Guid? customerId, AgreementStatus? status, AgreementHandler h, CancellationToken ct) => (await h.ListAsync(customerId, status, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsAgreements").Produces<IReadOnlyList<AgreementSummaryDto>>();
        api.MapGet("/agreements/{id:guid}", async (Guid id, AgreementHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsAgreement").Produces<AgreementDto>();
        api.MapPost("/agreements", async (SaveAgreementRequest body, AgreementHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Commercial).WithValidation<SaveAgreementRequest>().WithName("OpsCreateAgreement").Produces<AgreementDto>();
        api.MapPut("/agreements/{id:guid}", async (Guid id, SaveAgreementRequest body, AgreementHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Commercial).WithValidation<SaveAgreementRequest>().WithName("OpsUpdateAgreement").Produces<AgreementDto>();
        api.MapPost("/agreements/{id:guid}/activate", async (Guid id, AgreementHandler h, CancellationToken ct) => (await h.ActivateAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Commercial).WithName("OpsActivateAgreement").Produces<AgreementDto>();
        api.MapPost("/agreements/{id:guid}/revise", async (Guid id, ReviseAgreementRequest body, AgreementHandler h, CancellationToken ct) => (await h.ReviseAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Commercial).WithName("OpsReviseAgreement").Produces<AgreementDto>();
        api.MapDelete("/agreements/{id:guid}", async (Guid id, AgreementHandler h, CancellationToken ct) => (await h.CancelAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Commercial).WithName("OpsCancelAgreement").Produces(StatusCodes.Status204NoContent);
    }

    // ---------------------------------------------------------------- delivery requests
    private static void MapRequests(RouteGroupBuilder api)
    {
        api.MapGet("/requests", async (string? search, RequestStatus? status, Guid? customerId, int? page, int? pageSize, RequestHandler h, CancellationToken ct) =>
                (await h.ListAsync(search, status, customerId, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsRequests").Produces<PagedResult<RequestSummaryDto>>();
        api.MapGet("/requests/{id:guid}", async (Guid id, RequestHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsRequest").Produces<RequestDto>();
        api.MapPost("/requests", async (SaveRequestRequest body, RequestHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<SaveRequestRequest>().WithName("OpsCreateRequest").Produces<RequestDto>();
        api.MapPut("/requests/{id:guid}", async (Guid id, SaveRequestRequest body, RequestHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<SaveRequestRequest>().WithName("OpsUpdateRequest").Produces<RequestDto>();
        api.MapPost("/requests/{id:guid}/review", async (Guid id, RequestHandler h, CancellationToken ct) => (await h.StartReviewAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsReviewRequest").Produces<RequestDto>();
        api.MapPost("/requests/{id:guid}/validate", async (Guid id, RequestHandler h, CancellationToken ct) => (await h.ValidateAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsValidateRequest").Produces<RequestDto>();
        api.MapPost("/requests/{id:guid}/ready", async (Guid id, RequestHandler h, CancellationToken ct) => (await h.MarkReadyAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsReadyRequest").Produces<RequestDto>();
        api.MapPost("/requests/{id:guid}/cancel", async (Guid id, RequestHandler h, CancellationToken ct) => (await h.CancelAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsCancelRequest").Produces<RequestDto>();
        api.MapPost("/requests/{id:guid}/files", async (Guid id, HttpRequest request, RequestHandler h, CancellationToken ct) =>
            {
                var files = await ReadFilesAsync(request, ct);
                return files.Count != 1 ? Error.Validation("ops.file_count", "Send one file at a time.").ToProblem() : (await h.AttachAsync(id, files[0].Content, files[0].FileName, ct)).ToHttpResult();
            })
            .RequirePermission(OpsPermissions.Dispatch).DisableAntiforgery().WithName("OpsAttachRequestFile").Produces<FileDto>();
        api.MapGet("/request-files/{fileId:guid}", async (Guid fileId, RequestHandler h, CancellationToken ct) =>
            {
                var file = await h.OpenFileAsync(fileId, ct);
                return file.IsFailure ? file.Error.ToProblem() : Results.File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
            })
            .RequirePermission(OpsPermissions.Read).WithName("OpsRequestFile").Produces(StatusCodes.Status200OK);
    }

    // ---------------------------------------------------------------- LRs
    private static void MapLrs(RouteGroupBuilder api)
    {
        api.MapGet("/lrs", async (string? search, DeliveryStatus? status, PodStatus? podStatus, Guid? customerId, Guid? tripId, DateOnly? from, DateOnly? to, int? page, int? pageSize, LrHandler h, CancellationToken ct) =>
                (await h.ListAsync(search, status, podStatus, customerId, tripId, from, to, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsLrs").Produces<PagedResult<LrSummaryDto>>();
        api.MapGet("/lrs/{id:guid}", async (Guid id, LrHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsLr").Produces<LrDetailDto>();
        api.MapPost("/lrs", async (CreateLrRequest body, LrHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<CreateLrRequest>().WithName("OpsCreateLr").Produces<CreatedLrDto>();
        api.MapPost("/lrs/{id:guid}/files", async (Guid id, HttpRequest request, LrHandler h, CancellationToken ct) =>
            {
                var files = await ReadFilesAsync(request, ct);
                return files.Count != 1 ? Error.Validation("ops.file_count", "Send one file at a time.").ToProblem() : (await h.AttachInvoiceCopyAsync(id, files[0].Content, files[0].FileName, ct)).ToHttpResult();
            })
            .RequirePermission(OpsPermissions.Dispatch).DisableAntiforgery().WithName("OpsAttachLrFile").Produces<FileDto>();
        api.MapGet("/lrs/{id:guid}/print", async (Guid id, LrHandler h, CancellationToken ct) => (await h.PrintAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsPrintLr").Produces<LrPrintDto>();
        api.MapPost("/lrs/{id:guid}/qr", async (Guid id, LrHandler h, CancellationToken ct) => (await h.ReissueQrAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsReissueQr").Produces<CreatedLrDto>();
        api.MapDelete("/lrs/{id:guid}/qr", async (Guid id, LrHandler h, CancellationToken ct) => (await h.RevokeQrAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsRevokeQr").Produces<LrDetailDto>();
        api.MapPost("/lrs/{id:guid}/cancel", async (Guid id, LrHandler h, CancellationToken ct) => (await h.CancelAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsCancelLr").Produces<LrDetailDto>();
        api.MapPost("/lrs/{id:guid}/destination-events", async (Guid id, DestinationEventRequest body, DestinationEventsHandler h, CancellationToken ct) => (await h.RecordAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<DestinationEventRequest>().WithName("OpsDestinationEvent").Produces<LrDetailDto>();
        api.MapPost("/lrs/{id:guid}/destination-events/correct", async (Guid id, CorrectDestinationRequest body, DestinationEventsHandler h, CancellationToken ct) => (await h.CorrectAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<CorrectDestinationRequest>().WithName("OpsCorrectDestinationEvent").Produces<LrDetailDto>();
    }

    // ---------------------------------------------------------------- trips and the CFA timeline
    private static void MapTrips(RouteGroupBuilder api)
    {
        api.MapGet("/trips", async (string? search, TripStatus? status, int? page, int? pageSize, TripHandler h, CancellationToken ct) => (await h.ListAsync(search, status, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsTrips").Produces<PagedResult<TripSummaryDto>>();
        api.MapGet("/trips/{id:guid}", async (Guid id, TripHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsTrip").Produces<TripDetailDto>();
        api.MapPost("/trips", async (CreateTripRequest body, TripHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<CreateTripRequest>().WithName("OpsCreateTrip").Produces<TripDetailDto>();
        api.MapPut("/trips/{id:guid}", async (Guid id, UpdateTripRequest body, TripHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsUpdateTrip").Produces<TripDetailDto>();
        api.MapPost("/trips/{id:guid}/events", async (Guid id, CfaEventRequest body, [FromHeader(Name = "Idempotency-Key")] string? key, TripHandler h, CancellationToken ct) =>
                (await h.RecordCfaEventAsync(id, string.IsNullOrWhiteSpace(body.ClientKey) && key is not null ? body with { ClientKey = key } : body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<CfaEventRequest>().WithName("OpsTripEvent").Produces<TripDetailDto>();
        api.MapPost("/trips/{id:guid}/events/correct", async (Guid id, CorrectEventRequest body, TripHandler h, CancellationToken ct) => (await h.CorrectCfaEventAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithValidation<CorrectEventRequest>().WithName("OpsCorrectTripEvent").Produces<TripDetailDto>();
        api.MapPost("/trips/{id:guid}/cancel", async (Guid id, TripHandler h, CancellationToken ct) => (await h.CancelAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Dispatch).WithName("OpsCancelTrip").Produces<TripDetailDto>();
        api.MapGet("/vehicles", async (Guid? transporterId, TripHandler h, CancellationToken ct) => (await h.VehiclesAsync(transporterId, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsVehicles").Produces<IReadOnlyList<VehicleOptionDto>>();
        api.MapGet("/dashboard", async (DashboardHandler h, CancellationToken ct) => (await h.GetAsync(ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsDashboard").Produces<DashboardDto>();
    }

    // ---------------------------------------------------------------- proof of delivery (office)
    private static void MapPods(RouteGroupBuilder api)
    {
        api.MapGet("/pods", async (string? view, string? search, Guid? customerId, int? page, int? pageSize, PodHandler h, CancellationToken ct) =>
                (await h.QueueAsync(view, search, customerId, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsPods").Produces<PagedResult<PodQueueItemDto>>();
        api.MapGet("/lrs/{lrId:guid}/pod", async (Guid lrId, PodHandler h, CancellationToken ct) => (await h.GetByLrAsync(lrId, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsPod").Produces<PodDto>();
        api.MapPost("/lrs/{lrId:guid}/pod", async (Guid lrId, HttpRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, PodHandler h, CancellationToken ct) =>
            {
                var uploads = await ReadFilesAsync(request, ct);
                var remarks = request.HasFormContentType ? request.Form["remarks"].ToString() : null;
                return (await h.UploadAsync(lrId, uploads, remarks, key, ct)).ToHttpResult();
            })
            .RequirePermission(OpsPermissions.PodUpload).DisableAntiforgery().WithName("OpsUploadPod").Produces<PodDto>();
        api.MapPost("/lrs/{lrId:guid}/pod/start-review", async (Guid lrId, PodHandler h, CancellationToken ct) => (await h.StartReviewAsync(lrId, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.PodReview).WithName("OpsPodStartReview").Produces<PodDto>();
        api.MapPost("/lrs/{lrId:guid}/pod/accept", async (Guid lrId, PodDecisionRequest body, PodHandler h, CancellationToken ct) => (await h.AcceptAsync(lrId, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.PodReview).WithValidation<PodDecisionRequest>().WithName("OpsPodAccept").Produces<PodDto>();
        api.MapPost("/lrs/{lrId:guid}/pod/reject", async (Guid lrId, PodDecisionRequest body, PodHandler h, CancellationToken ct) => (await h.RejectAsync(lrId, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.PodReview).WithValidation<PodDecisionRequest>().WithName("OpsPodReject").Produces<PodDto>();
        api.MapPost("/lrs/{lrId:guid}/pod/reopen", async (Guid lrId, PodDecisionRequest body, PodHandler h, CancellationToken ct) => (await h.ReopenAsync(lrId, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.PodReview).WithValidation<PodDecisionRequest>().WithName("OpsPodReopen").Produces<PodDto>();
        api.MapGet("/pod-documents/{documentId:guid}/file", async (Guid documentId, PodHandler h, CancellationToken ct) =>
            {
                var file = await h.OpenDocumentAsync(documentId, ct);
                return file.IsFailure ? file.Error.ToProblem() : Results.File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
            })
            .RequirePermission(OpsPermissions.Read).WithName("OpsPodFile").Produces(StatusCodes.Status200OK);
    }

    // ---------------------------------------------------------------- freight and detention
    private static void MapBilling(RouteGroupBuilder api)
    {
        api.MapGet("/billing", async (Guid? customerId, DateOnly? from, DateOnly? to, string? search, PodStatus? podStatus, CalculationStatus? calcStatus, bool? invoiced, int? page, int? pageSize, BillingHandler h, CancellationToken ct) =>
                (await h.WorkbenchAsync(customerId, from, to, search, podStatus, calcStatus, invoiced, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsBilling").Produces<PagedResult<BillingRowDto>>();
        api.MapPost("/billing/calculate", async (CalculateRequest body, BillingHandler h, CancellationToken ct) => (await h.CalculateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingCalculate).WithValidation<CalculateRequest>().WithName("OpsCalculate").Produces<CalculateResultDto>();
        api.MapGet("/billing/calculations/{id:guid}", async (Guid id, BillingHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsCalculation").Produces<CalculationDto>();
        api.MapGet("/lrs/{lrId:guid}/calculations", async (Guid lrId, BillingHandler h, CancellationToken ct) => (await h.ForLrAsync(lrId, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsLrCalculations").Produces<IReadOnlyList<CalculationDto>>();
        api.MapPost("/billing/calculations/{id:guid}/review", async (Guid id, ReviewCalculationRequest body, BillingHandler h, CancellationToken ct) => (await h.ReviewAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingCalculate).WithName("OpsReviewCalculation").Produces<CalculationDto>();
        api.MapPost("/billing/calculations/{id:guid}/resolve-hold", async (Guid id, ResolveHoldRequest body, BillingHandler h, CancellationToken ct) => (await h.ResolveHoldAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingCalculate).WithValidation<ResolveHoldRequest>().WithName("OpsResolveHold").Produces<CalculationDto>();
    }

    // ---------------------------------------------------------------- invoices, submissions, payments
    private static void MapInvoices(RouteGroupBuilder api)
    {
        api.MapGet("/invoices", async (string? search, Guid? customerId, InvoiceStatus? status, SubmissionStatus? submission, PaymentState? payment, DateOnly? from, DateOnly? to, int? page, int? pageSize, InvoiceHandler h, CancellationToken ct) =>
                (await h.ListAsync(search, customerId, status, submission, payment, from, to, page ?? 1, pageSize ?? 25, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsInvoices").Produces<PagedResult<InvoiceSummaryDto>>();
        api.MapGet("/invoices/{id:guid}", async (Guid id, InvoiceHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsInvoice").Produces<InvoiceDto>();
        api.MapPost("/invoices", async (CreateInvoiceRequest body, InvoiceHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.InvoiceManage).WithValidation<CreateInvoiceRequest>().WithName("OpsCreateInvoice").Produces<InvoiceDto>();
        api.MapPost("/invoices/{id:guid}/cancel", async (Guid id, CancelInvoiceRequest body, InvoiceHandler h, CancellationToken ct) => (await h.CancelAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.InvoiceManage).WithValidation<CancelInvoiceRequest>().WithName("OpsCancelInvoice").Produces<InvoiceDto>();
        api.MapPost("/invoices/{id:guid}/submit", async (Guid id, SubmitInvoiceRequest body, InvoiceHandler h, CancellationToken ct) => (await h.SubmitAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.InvoiceManage).WithValidation<SubmitInvoiceRequest>().WithName("OpsSubmitInvoice").Produces<InvoiceDto>();
        api.MapPost("/invoices/{id:guid}/acknowledge", async (Guid id, AcknowledgeSubmissionRequest body, InvoiceHandler h, CancellationToken ct) => (await h.AcknowledgeAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.InvoiceManage).WithName("OpsAcknowledgeInvoice").Produces<InvoiceDto>();
        api.MapPost("/invoices/{id:guid}/reject-submission", async (Guid id, RejectSubmissionRequest body, InvoiceHandler h, CancellationToken ct) => (await h.RejectSubmissionAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.InvoiceManage).WithValidation<RejectSubmissionRequest>().WithName("OpsRejectSubmission").Produces<InvoiceDto>();
        api.MapPost("/invoices/{id:guid}/payments", async (Guid id, PaymentRequest body, InvoiceHandler h, CancellationToken ct) => (await h.RecordPaymentAsync(id, body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.PaymentManage).WithValidation<PaymentRequest>().WithName("OpsRecordPayment").Produces<InvoiceDto>();
        api.MapGet("/receivables", async (DateOnly? asOf, InvoiceHandler h, CancellationToken ct) => (await h.ReceivablesAsync(asOf, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.BillingRead).WithName("OpsReceivables").Produces<IReadOnlyList<ReceivableRowDto>>();
    }

    private static void MapSettings(RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (SettingsHandler h, CancellationToken ct) => (await h.GetAsync(ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Read).WithName("OpsSettings").Produces<OpsSettingsDto>();
        api.MapPut("/settings", async (OpsSettingsDto body, SettingsHandler h, CancellationToken ct) => (await h.SaveAsync(body, ct)).ToHttpResult())
            .RequirePermission(OpsPermissions.Configure).WithValidation<OpsSettingsDto>().WithName("OpsSaveSettings").Produces<OpsSettingsDto>();
    }
}
