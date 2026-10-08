using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Reports.Application;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Reports.Endpoints;

/// <summary>All routes require sign-in; who may open, export or change what is decided inside the handlers (<see cref="ReportAccess"/>), so a missed check can never be a missed route.</summary>
internal static class ReportEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Reports & Analytics").RequireAuthorization();
        MapCatalogue(api);
        MapJobs(api);
        MapAdmin(api);
        MapKpis(api);
        MapDashboards(api);
        MapSubscriptions(api);
    }

    private static Dictionary<string, JsonElement> Query(HttpRequest request, params string[] skip) =>
        request.Query.Where(q => !skip.Contains(q.Key, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(q.Value.ToString()))
            .ToDictionary(q => q.Key, q => JsonSerializer.SerializeToElement(q.Value.ToString()), StringComparer.OrdinalIgnoreCase);

    private static void MapCatalogue(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/reports");
        g.MapGet("/", async (string? search, string? category, CatalogueHandler h, CancellationToken ct) => (await h.ListAsync(search, category, ct)).ToHttpResult()).WithName("ListReports").Produces<IReadOnlyList<ReportSummaryDto>>();
        g.MapGet("/{code}", async (string code, CatalogueHandler h, CancellationToken ct) => (await h.MetadataAsync(code, ct)).ToHttpResult()).WithName("GetReport").Produces<ReportMetadataDto>();
        g.MapGet("/{code}/metadata", async (string code, CatalogueHandler h, CancellationToken ct) => (await h.MetadataAsync(code, ct)).ToHttpResult()).WithName("GetReportMetadata").Produces<ReportMetadataDto>();
        g.MapPost("/{code}/execute", async (string code, ReportRequest body, ReportRunHandler h, CancellationToken ct) => (await h.ExecuteAsync(code, body, ct)).ToHttpResult()).WithName("ExecuteReport").Produces<ReportResult>();
        g.MapPost("/{code}/export", async (string code, ExportRequest body, ExportHandler h, CancellationToken ct) =>
        {
            var result = await h.ExportAsync(code, body, ct);
            return result.IsFailure ? result.Error.ToProblem() : result.Value.Immediate ? Results.Ok(result.Value) : Results.Accepted($"/api/v1/reports/jobs/{result.Value.Job.Id}", result.Value);
        }).WithName("ExportReport").Produces<ExportOutcome>().Produces<ExportOutcome>(StatusCodes.Status202Accepted);
        g.MapGet("/lookups/{name}", async (string name, LookupHandler h, CancellationToken ct) => (await h.GetAsync(name, ct)).ToHttpResult()).WithName("ReportLookup").Produces<LookupDto>();
        g.MapGet("/preferences", async (PreferenceHandler h, CancellationToken ct) => (await h.GetAsync(ct)).ToHttpResult()).WithName("GetReportPreferences").Produces<PreferenceDto>();
        g.MapPut("/preferences", async (SavePreferenceRequest body, PreferenceHandler h, CancellationToken ct) => (await h.SaveAsync(body, ct)).ToHttpResult()).WithName("SaveReportPreferences").Produces<PreferenceDto>();
    }

    private static void MapJobs(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/reports/jobs");
        g.MapGet("/", async (ExportHandler h, CancellationToken ct) => (await h.ListAsync(ct)).ToHttpResult()).WithName("ListReportJobs").Produces<IReadOnlyList<ReportJobDto>>();
        g.MapGet("/{id:guid}", async (Guid id, ExportHandler h, CancellationToken ct) => (await h.GetAsync(id, ct)).ToHttpResult()).WithName("GetReportJob").Produces<ReportJobDto>();
        g.MapPost("/{id:guid}/cancel", async (Guid id, ExportHandler h, CancellationToken ct) => (await h.CancelAsync(id, ct)).ToHttpResult()).WithName("CancelReportJob").Produces<ReportJobDto>();
        g.MapPost("/{id:guid}/retry", async (Guid id, ExportHandler h, CancellationToken ct) => (await h.RetryAsync(id, ct)).ToHttpResult()).WithName("RetryReportJob").Produces<ReportJobDto>();
        g.MapGet("/{id:guid}/download", async (Guid id, HttpContext http, ExportHandler h, CancellationToken ct) =>
        {
            var result = await h.DownloadAsync(id, ct);
            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        }).WithName("DownloadReportJob").Produces(StatusCodes.Status200OK);
    }

    private static void MapAdmin(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/reports");
        g.MapGet("/data-source", async (AdminHandler h, CancellationToken ct) => (await h.DataSourceAsync(ct)).ToHttpResult()).WithName("ReportDataSource").Produces<DataSourceStatusDto>();
        g.MapGet("/settings", async (AdminHandler h, CancellationToken ct) => (await h.GetSettingsAsync(ct)).ToHttpResult()).WithName("GetReportSettings").Produces<SettingsDto>();
        g.MapPut("/settings", async (SaveSettingsRequest body, AdminHandler h, CancellationToken ct) => (await h.SaveSettingsAsync(body, ct)).ToHttpResult()).WithName("SaveReportSettings").Produces<SettingsDto>();
        g.MapPut("/{code}/definition", async (string code, UpdateReportRequest body, AdminHandler h, CancellationToken ct) => (await h.UpdateReportAsync(code, body, ct)).ToHttpResult()).WithName("UpdateReportDefinition").Produces<ReportMetadataDto>();
        g.MapGet("/scopes", async (Guid? userId, AdminHandler h, CancellationToken ct) => (await h.ScopesAsync(userId, ct)).ToHttpResult()).WithName("ListReportScopes").Produces<IReadOnlyList<DataScopeDto>>();
        g.MapPut("/scopes", async (SaveDataScopeRequest body, AdminHandler h, CancellationToken ct) => (await h.SaveScopeAsync(body, ct)).ToHttpResult()).WithName("SaveReportScope").Produces<DataScopeDto>();
        g.MapGet("/audit", async (string? report, string? action, Guid? by, DateOnly? from, DateOnly? to, int? page, int? pageSize, AdminHandler h, CancellationToken ct) =>
            (await h.AuditAsync(report, action, by, from, to, page ?? 1, pageSize ?? 50, ct)).ToHttpResult()).WithName("ReportAudit").Produces<PagedResult<AuditEntryDto>>();
        g.MapPost("/summary/rebuild", async (int? days, AdminHandler h, CancellationToken ct) => (await h.RebuildSummaryAsync(days ?? 30, ct)).ToHttpResult()).WithName("RebuildReportSummary").Produces<int>();
    }

    private static void MapKpis(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/kpis");
        g.MapGet("/", async (CatalogueHandler h, CancellationToken ct) => (await h.KpisAsync(ct)).ToHttpResult()).WithName("ListKpis").Produces<IReadOnlyList<KpiDefinitionDto>>();
        g.MapGet("/{code}", async (string code, CatalogueHandler h, CancellationToken ct) => (await h.KpiAsync(code, ct)).ToHttpResult()).WithName("GetKpi").Produces<KpiDefinitionDto>();
        g.MapGet("/{code}/value", async (string code, string? period, HttpRequest http, IKpiCalculationService s, CancellationToken ct) =>
            (await s.CalculateAsync(new KpiRequest(code, Query(http, "period"), period), ct)).ToHttpResult()).WithName("CalculateKpi").Produces<KpiOutcome>();
        g.MapPost("/calculate", async (KpiRequest body, IKpiCalculationService s, CancellationToken ct) => (await s.CalculateAsync(body, ct)).ToHttpResult()).WithName("CalculateKpiBody").Produces<KpiOutcome>();
        g.MapPut("/{code}", async (string code, UpdateKpiRequest body, AdminHandler h, CancellationToken ct) => (await h.UpdateKpiAsync(code, body, ct)).ToHttpResult()).WithName("UpdateKpi").Produces<KpiDefinitionDto>();
    }

    private static void MapDashboards(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/dashboards");
        g.MapGet("/executive", async (HttpRequest http, ReportRunHandler h, CancellationToken ct) =>
            (await h.DashboardAsync("R01_EXECUTIVE_DASHBOARD", Query(http, "refresh"), http.Query.ContainsKey("refresh"), ct)).ToHttpResult()).WithName("ExecutiveDashboard").Produces<ReportResult>();
        g.MapGet("/control-tower", async (HttpRequest http, ReportRunHandler h, CancellationToken ct) =>
            (await h.DashboardAsync("R24_CONTROL_TOWER", Query(http, "refresh"), http.Query.ContainsKey("refresh"), ct)).ToHttpResult()).WithName("ControlTowerDashboard").Produces<ReportResult>();
    }

    private static void MapSubscriptions(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/report-subscriptions");
        g.MapGet("/", async (bool? all, SubscriptionHandler h, CancellationToken ct) => (await h.ListAsync(all ?? false, ct)).ToHttpResult()).WithName("ListReportSubscriptions").Produces<IReadOnlyList<SubscriptionDto>>();
        g.MapPost("/", async (SaveSubscriptionRequest body, SubscriptionHandler h, CancellationToken ct) => (await h.CreateAsync(body, ct)).ToCreatedResult(s => $"/api/v1/report-subscriptions/{s.Id}")).WithName("CreateReportSubscription").Produces<SubscriptionDto>(StatusCodes.Status201Created);
        g.MapPut("/{id:guid}", async (Guid id, SaveSubscriptionRequest body, SubscriptionHandler h, CancellationToken ct) => (await h.UpdateAsync(id, body, ct)).ToHttpResult()).WithName("UpdateReportSubscription").Produces<SubscriptionDto>();
        g.MapPost("/{id:guid}/run-now", async (Guid id, SubscriptionHandler h, CancellationToken ct) => (await h.RunNowAsync(id, ct)).ToHttpResult()).WithName("RunReportSubscriptionNow").Produces<ReportJobDto>();
        g.MapDelete("/{id:guid}", async (Guid id, SubscriptionHandler h, CancellationToken ct) => (await h.DeleteAsync(id, ct)).ToHttpResult()).WithName("DeleteReportSubscription").Produces(StatusCodes.Status204NoContent);
    }
}
