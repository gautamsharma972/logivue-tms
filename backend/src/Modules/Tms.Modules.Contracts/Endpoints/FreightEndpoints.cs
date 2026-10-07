using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Analytics;
using Tms.Modules.Contracts.Application.Contracts;
using Tms.Modules.Contracts.Application.Import;
using Tms.Modules.Contracts.Application.Masters;
using Tms.Modules.Contracts.Application.Rates;
using Tms.Modules.Contracts.Application.Rating;
using Tms.Modules.Contracts.Application.Terms;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Endpoints;

/// <summary>Multipart fields of a rate sheet upload.</summary>
public sealed class UploadRatesForm
{
    public IFormFile? File { get; init; }

    public ImportMode Mode { get; init; } = ImportMode.Append;
}

/// <summary>The freight contract management surface: lifecycle, rates, rating, DPH, extra charges, bulk maintenance, dashboards and reports. All routes require sign-in; handlers enforce permissions.</summary>
internal static class FreightEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Freight contracts").RequireAuthorization();
        MapLifecycle(api);
        MapSpecContractRoutes(api);
        MapTerms(api);
        MapRates(api);
        MapImport(api);
        MapRating(api);
        MapDph(api);
        MapAnalytics(api);
    }

    private static IResult File(Result<SheetFile> result) => result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();

    private static void MapLifecycle(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/contracts");
        g.MapPost("/{id:guid}/suspend", async (Guid id, ContractReasonRequest body, SuspendContractHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ContractReasonRequest>().WithName("SuspendContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/resume", async (Guid id, ResumeContractHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult()).WithName("ResumeContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/cancel", async (Guid id, ContractReasonRequest body, CancelContractHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ContractReasonRequest>().WithName("CancelContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/renew", async (Guid id, RenewRequest? body, RenewContractHandler h, CancellationToken ct) =>
                (await h.HandleAsync(id, body ?? new RenewRequest(null, null, null), ct)).ToCreatedResult(c => $"/api/v1/contracts/{c.Summary.Id}"))
            .WithName("RenewContract").Produces<ContractDto>(StatusCodes.Status201Created);
        g.MapPost("/{id:guid}/approve", async (Guid id, DecisionBody? body, DecideContractHandler h, CancellationToken ct) => (await h.ApproveAsync(id, body ?? new DecisionBody(null), ct)).ToHttpResult())
            .WithName("ApproveContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/reject", async (Guid id, DecisionBody? body, DecideContractHandler h, CancellationToken ct) => (await h.RejectAsync(id, body ?? new DecisionBody(null), ct)).ToHttpResult())
            .WithName("RejectContract").Produces<ContractDto>();
        g.MapGet("/{id:guid}/renewal-impact", async (Guid id, int? months, ImpactHandler h, CancellationToken ct) => (await h.HandleAsync(id, months, ct)).ToHttpResult())
            .WithName("ContractRenewalImpact").Produces<ImpactDto>();
        g.MapPost("/{id:guid}/documents/{documentId:guid}/verify", async (Guid id, Guid documentId, DocumentHandler h, CancellationToken ct) => (await h.VerifyAsync(id, documentId, ct)).ToHttpResult())
            .WithName("VerifyContractDocument").Produces<ContractDocumentDto>();
    }

    /// <summary>The same operations under the spec's names. Each is a second route to the same handler.</summary>
    private static void MapSpecContractRoutes(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/freight-contracts");
        g.MapGet("/", async ([AsParameters] ListContractsQuery query, ListContractsHandler h, CancellationToken ct) => (await h.HandleAsync(query, ct)).ToHttpResult()).WithName("FcListContracts").Produces<PagedResult<ContractSummaryDto>>();
        g.MapPost("/", async (SaveContractRequest body, CreateContractHandler h, CancellationToken ct) => (await h.HandleAsync(body, ct)).ToCreatedResult(c => $"/api/v1/freight-contracts/{c.Summary.Id}"))
            .WithValidation<SaveContractRequest>().WithName("FcCreateContract").Produces<ContractDto>(StatusCodes.Status201Created);
        g.MapGet("/{id:guid}", async (Guid id, GetContractHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult()).WithName("FcGetContract").Produces<ContractDto>();
        g.MapPut("/{id:guid}", async (Guid id, SaveContractRequest body, UpdateContractHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveContractRequest>().WithName("FcUpdateContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/submit", async (Guid id, SubmitContractHandler h, CancellationToken ct) => (await h.HandleAsync(id, ct)).ToHttpResult()).WithName("FcSubmitContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/approve", async (Guid id, DecisionBody? body, DecideContractHandler h, CancellationToken ct) => (await h.ApproveAsync(id, body ?? new DecisionBody(null), ct)).ToHttpResult()).WithName("FcApproveContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/reject", async (Guid id, DecisionBody? body, DecideContractHandler h, CancellationToken ct) => (await h.RejectAsync(id, body ?? new DecisionBody(null), ct)).ToHttpResult()).WithName("FcRejectContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/suspend", async (Guid id, ContractReasonRequest body, SuspendContractHandler h, CancellationToken ct) => (await h.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<ContractReasonRequest>().WithName("FcSuspendContract").Produces<ContractDto>();
        g.MapPost("/{id:guid}/renew", async (Guid id, RenewRequest? body, RenewContractHandler h, CancellationToken ct) =>
                (await h.HandleAsync(id, body ?? new RenewRequest(null, null, null), ct)).ToCreatedResult(c => $"/api/v1/freight-contracts/{c.Summary.Id}"))
            .WithName("FcRenewContract").Produces<ContractDto>(StatusCodes.Status201Created);
        g.MapPost("/{id:guid}/create-version", async (Guid id, ReviseRequest body, ReviseContractHandler h, CancellationToken ct) =>
                (await h.HandleAsync(id, body, ct)).ToCreatedResult(c => $"/api/v1/freight-contracts/{c.Summary.Id}"))
            .WithName("FcCreateContractVersion").Produces<ContractDto>(StatusCodes.Status201Created);
        g.MapGet("/{id:guid}/documents", async (Guid id, DocumentHandler h, CancellationToken ct) => (await h.ListAsync(id, ct)).ToHttpResult()).WithName("FcListDocuments").Produces<IReadOnlyList<ContractDocumentDto>>();
        g.MapPost("/{id:guid}/documents", async (Guid id, [FromForm] UploadContractDocumentForm form, DocumentHandler h, CancellationToken ct) =>
                (await h.UploadAsync(id, form, ct)).ToCreatedResult(d => $"/api/v1/contract-documents/{d.Id}"))
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024)).WithName("FcUploadDocument").Accepts<UploadContractDocumentForm>("multipart/form-data").Produces<ContractDocumentDto>(StatusCodes.Status201Created);
        g.MapPost("/{id:guid}/documents/{documentId:guid}/verify", async (Guid id, Guid documentId, DocumentHandler h, CancellationToken ct) => (await h.VerifyAsync(id, documentId, ct)).ToHttpResult())
            .WithName("FcVerifyDocument").Produces<ContractDocumentDto>();
    }

    private static void MapTerms(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/contracts/{id:guid}");
        g.MapGet("/dph-rules", async (Guid id, ContractTermsHandler h, CancellationToken ct) => (await h.GetDphAsync(id, ct)).ToHttpResult()).WithName("GetContractDphRules").Produces<IReadOnlyList<DphRuleSpec>>();
        g.MapPut("/dph-rules", async (Guid id, SaveDphRulesRequest body, ContractTermsHandler h, CancellationToken ct) => (await h.SaveDphAsync(id, body, ct)).ToHttpResult()).WithName("SaveContractDphRules").Produces<ContractDto>();
        g.MapGet("/accessorials", async (Guid id, ContractTermsHandler h, CancellationToken ct) => (await h.GetAccessorialsAsync(id, ct)).ToHttpResult()).WithName("GetContractAccessorials").Produces<IReadOnlyList<AccessorialSpec>>();
        g.MapPut("/accessorials", async (Guid id, SaveAccessorialsRequest body, ContractTermsHandler h, CancellationToken ct) => (await h.SaveAccessorialsAsync(id, body, ct)).ToHttpResult()).WithName("SaveContractAccessorials").Produces<ContractDto>();
        g.MapGet("/capacity", async (Guid id, ContractTermsHandler h, CancellationToken ct) => (await h.GetCapacityAsync(id, ct)).ToHttpResult()).WithName("GetContractCapacity").Produces<IReadOnlyList<CapacityDto>>();
        g.MapPut("/capacity", async (Guid id, SaveCapacityRequest body, ContractTermsHandler h, CancellationToken ct) => (await h.SaveCapacityAsync(id, body, ct)).ToHttpResult()).WithName("SaveContractCapacity").Produces<ContractDto>();
        g.MapGet("/sla", async (Guid id, ContractTermsHandler h, CancellationToken ct) => (await h.GetSlaAsync(id, ct)).ToHttpResult()).WithName("GetContractSla").Produces<IReadOnlyList<SlaSpec>>();
        g.MapPut("/sla", async (Guid id, SaveSlaRequest body, ContractTermsHandler h, CancellationToken ct) => (await h.SaveSlaAsync(id, body, ct)).ToHttpResult()).WithName("SaveContractSla").Produces<ContractDto>();

        var a = api.MapGroup("/accessorials");
        a.MapGet("/", async (AccessorialTypeHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult()).WithName("ListAccessorials").Produces<IReadOnlyList<AccessorialTypeDto>>();
        a.MapPost("/", async (SaveAccessorialTypeRequest body, AccessorialTypeHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToCreatedResult(x => $"/api/v1/accessorials/{x.Id}"))
            .WithValidation<SaveAccessorialTypeRequest>().WithName("CreateAccessorial").Produces<AccessorialTypeDto>(StatusCodes.Status201Created);
        a.MapPut("/{accessorialId:guid}", async (Guid accessorialId, SaveAccessorialTypeRequest body, AccessorialTypeHandler h, CancellationToken ct) => (await h.UpdateAsync(accessorialId, body, ct)).ToHttpResult())
            .WithValidation<SaveAccessorialTypeRequest>().WithName("UpdateAccessorial").Produces<AccessorialTypeDto>();
    }

    private static void MapRates(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/freight-rates");
        g.MapGet("/", async ([AsParameters] ListRatesQuery query, RateHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult()).WithName("ListFreightRates").Produces<PagedResult<RateRowDto>>();
        g.MapPost("/", async (CreateRateRequest body, RateHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToCreatedResult(c => $"/api/v1/freight-contracts/{c.Summary.Id}"))
            .WithName("CreateFreightRate").Produces<ContractDto>(StatusCodes.Status201Created);
        g.MapPost("/validate", async (ValidateRatesRequest body, RateHandler h, CancellationToken ct) => (await h.ValidateAsync(body, ct)).ToHttpResult()).WithName("ValidateFreightRates").Produces<RateValidationDto>();
        g.MapGet("/{id:guid}", async (Guid id, RateHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult()).WithName("GetFreightRate").Produces<RateRowDto>();
        g.MapGet("/{id:guid}/history", async (Guid id, RateHandler h, CancellationToken ct) => (await h.HistoryAsync(id, ct)).ToHttpResult()).WithName("GetFreightRateHistory").Produces<IReadOnlyList<RateRowDto>>();
        g.MapPost("/{id:guid}/create-version", async (Guid id, CreateRateVersionRequest body, RateHandler h, CancellationToken ct) => (await h.CreateVersionAsync(id, body, ct)).ToHttpResult())
            .WithName("CreateFreightRateVersion").Produces<ContractDto>();
        g.MapGet("/export", async ([AsParameters] ListRatesQuery query, string? format, ImportHandler h, CancellationToken ct) => File(await h.ExportAsync(query, format, ct))).WithName("ExportFreightRates").Produces(StatusCodes.Status200OK);
    }

    private static void MapImport(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/freight-rates/import");
        g.MapGet("/template", async (string? format, ImportHandler h, CancellationToken ct) => File(await h.TemplateAsync(format, ct))).WithName("FreightRateTemplate").Produces(StatusCodes.Status200OK);
        g.MapGet("/", async (ImportHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult()).WithName("ListRateImports").Produces<IReadOnlyList<ImportBatchDto>>();
        g.MapPost("/", async ([FromForm] UploadRatesForm form, ImportHandler h, CancellationToken ct) =>
            {
                if (form.File is not { Length: > 0 } file)
                {
                    return Error.Validation("rate_import.file_required", "Attach the rate sheet.").ToProblem();
                }

                await using var stream = file.OpenReadStream();
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, ct);
                copy.Position = 0;
                return (await h.UploadAsync(copy, file.FileName, form.Mode, ct)).ToCreatedResult(b => $"/api/v1/freight-rates/import/{b.Id}");
            })
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(7 * 1024 * 1024)).WithName("UploadRateSheet").Accepts<UploadRatesForm>("multipart/form-data").Produces<ImportBatchDto>(StatusCodes.Status201Created);
        g.MapGet("/{id:guid}", async (Guid id, ImportHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult()).WithName("GetRateImport").Produces<ImportBatchDto>();
        g.MapPost("/{id:guid}/rows/{row:int}", async (Guid id, int row, CorrectImportRowRequest body, ImportHandler h, CancellationToken ct) => (await h.CorrectRowAsync(id, row, body, ct)).ToHttpResult())
            .WithName("CorrectRateImportRow").Produces<ImportBatchDto>();
        g.MapPost("/{id:guid}/apply", async (Guid id, ApplyImportRequest? body, ImportHandler h, CancellationToken ct) => (await h.ApplyAsync(id, body ?? new ApplyImportRequest(), ct)).ToHttpResult())
            .WithName("ApplyRateImport").Produces<ImportApplyResult>();
        g.MapDelete("/{id:guid}", async (Guid id, ImportHandler h, CancellationToken ct) => (await h.DiscardAsync(id, ct)).ToHttpResult()).WithName("DiscardRateImport").Produces<ImportBatchDto>();
    }

    private static void MapRating(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/freight-rating");
        g.MapPost("/calculate", async (RatingRequestDto body, FreightRatingHandler h, CancellationToken ct) => (await h.CalculateAsync(body, ct)).ToHttpResult())
            .WithValidation<RatingRequestDto>().WithName("CalculateFreight").Produces<RatingResultDto>();
        g.MapPost("/qualify", async (RatingRequestDto body, FreightRatingHandler h, CancellationToken ct) => (await h.QualifyAsync(body, ct)).ToHttpResult())
            .WithValidation<RatingRequestDto>().WithName("QualifyFreight").Produces<RatingResultDto>();
        g.MapPost("/simulate", async (RatingRequestDto body, FreightRatingHandler h, CancellationToken ct) => (await h.SimulateAsync(body, ct)).ToHttpResult())
            .WithValidation<RatingRequestDto>().WithName("SimulateFreight").Produces<RatingResultDto>();
        g.MapPost("/what-if", async (WhatIfRequest body, FreightRatingHandler h, CancellationToken ct) => (await h.WhatIfAsync(body, ct)).ToHttpResult())
            .WithValidation<WhatIfRequest>().WithName("WhatIfFreight").Produces<WhatIfResultDto>();
        g.MapPost("/compare", async (CompareRequest body, FreightRatingHandler h, CancellationToken ct) => (await h.CompareAsync(body, ct)).ToHttpResult())
            .WithValidation<CompareRequest>().WithName("CompareFreight").Produces<IReadOnlyList<CompareRowDto>>();
        g.MapGet("/history", async ([AsParameters] ListRatingsQuery query, FreightRatingHandler h, CancellationToken ct) => (await h.ListAsync(query, ct)).ToHttpResult()).WithName("FreightRatingHistory").Produces<PagedResult<RatingSummaryDto>>();
        g.MapGet("/{id:guid}", async (Guid id, FreightRatingHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult()).WithName("GetFreightRating").Produces<RatingDetailDto>();
        g.MapGet("/{id:guid}/reproduce", async (Guid id, FreightRatingHandler h, CancellationToken ct) => (await h.ReproduceAsync(id, ct)).ToHttpResult()).WithName("ReproduceFreightRating").Produces<ReproduceDto>();
        g.MapPost("/{id:guid}/override", async (Guid id, OverrideRatingRequest body, FreightRatingHandler h, CancellationToken ct) => (await h.OverrideAsync(id, body, ct)).ToHttpResult())
            .WithValidation<OverrideRatingRequest>().WithName("OverrideFreightRating").Produces<RatingDetailDto>();
        g.MapDelete("/{id:guid}/override", async (Guid id, FreightRatingHandler h, CancellationToken ct) => (await h.ClearOverrideAsync(id, ct)).ToHttpResult()).WithName("ClearFreightRatingOverride").Produces<RatingDetailDto>();
    }

    private static void MapDph(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/dph");
        g.MapGet("/rules", async (Guid? contractId, bool? inForceOnly, DphHandler h, CancellationToken ct) => (await h.ListAsync(contractId, inForceOnly, ct)).ToHttpResult()).WithName("ListDphRules").Produces<IReadOnlyList<DphOverviewDto>>();
        g.MapPost("/rules", async (SaveDphRuleRequest body, DphHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToHttpResult()).WithName("CreateDphRule").Produces<ContractDto>();
        g.MapPut("/rules/{id:guid}", async (Guid id, SaveDphRuleRequest body, DphHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult()).WithName("UpdateDphRule").Produces<ContractDto>();
        g.MapPost("/rules/{id:guid}/calculate", async (Guid id, DphCalculateRequest body, DphHandler h, CancellationToken ct) => (await h.CalculateAsync(id, body, ct)).ToHttpResult()).WithName("CalculateDphRule").Produces<DphCalculationDto>();
        g.MapGet("/price-index", async (string? region, DphHandler h, CancellationToken ct) => (await h.ListPricesAsync(region, ct)).ToHttpResult()).WithName("ListFuelPriceIndex").Produces<IReadOnlyList<PriceIndexDto>>();
        g.MapPost("/price-index", async (AddPriceIndexRequest body, AddDieselPriceHandler h, CancellationToken ct) =>
                (await h.HandleAsync(new AddDieselPriceRequest(body.Region, body.ReferenceDate, body.Price, body.Source), ct)).ToCreatedResult(p => $"/api/v1/dph/price-index/{p.Id}"))
            .WithValidation<AddPriceIndexRequest>().WithName("AddFuelPriceIndex").Produces<DieselPriceDto>(StatusCodes.Status201Created);
    }

    private static void MapAnalytics(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/freight-contract-dashboard");
        g.MapGet("/summary", async (DashboardHandler h, CancellationToken ct) => (await h.SummaryAsync(ct)).ToHttpResult()).WithName("ContractDashboardSummary").Produces<ContractDashboardDto>();
        g.MapGet("/expiry", async (int? withinDays, DashboardHandler h, CancellationToken ct) => (await h.ExpiryAsync(withinDays, ct)).ToHttpResult()).WithName("ContractDashboardExpiry").Produces<ExpiryDto>();
        g.MapGet("/rate-coverage", async (DashboardHandler h, CancellationToken ct) => (await h.CoverageAsync(ct)).ToHttpResult()).WithName("ContractDashboardCoverage").Produces<RateCoverageDto>();
        g.MapGet("/validation", async (DashboardHandler h, CancellationToken ct) => (await h.ValidationAsync(ct)).ToHttpResult()).WithName("ContractDashboardValidation").Produces<ValidationOverviewDto>();
        g.MapGet("/rate-usage", async (int? months, Guid? transporterId, DashboardHandler h, CancellationToken ct) => (await h.UsageAsync(months, transporterId, ct)).ToHttpResult()).WithName("ContractDashboardUsage").Produces<IReadOnlyList<RateUsageDto>>();

        var r = api.MapGroup("/freight-contract-reports");
        r.MapGet("/", () => Results.Ok(new ReportsListDto(ContractReportsHandler.Reports))).WithName("ListContractReports").Produces<ReportsListDto>();
        r.MapGet("/{report}", async (string report, string? format, DateOnly? from, DateOnly? to, Guid? transporterId, ContractReportsHandler h, CancellationToken ct) => File(await h.RunAsync(report, format, from, to, transporterId, ct)))
            .WithName("ContractReport").Produces(StatusCodes.Status200OK);
    }
}
