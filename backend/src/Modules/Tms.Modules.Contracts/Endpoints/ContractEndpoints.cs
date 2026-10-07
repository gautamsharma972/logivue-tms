using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Tms.BuildingBlocks.Web.Http;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Contracts;
using Tms.Modules.Contracts.Application.Masters;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Contracts.Endpoints;

/// <summary>All routes require sign-in; permissions and the staff-only rule are enforced inside the handlers (<see cref="ContractAccess"/>).</summary>
internal static class ContractEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Contracts").RequireAuthorization();
        MapContracts(api);
        MapMasters(api);
        MapDocuments(api);
        FreightEndpoints.Map(app);

        api.MapPost("/freight/quote", async (QuoteRequest body, QuoteHandler handler, CancellationToken ct) => (await handler.HandleAsync(body, ct)).ToHttpResult())
            .WithValidation<QuoteRequest>().WithName("QuoteFreight").Produces<QuoteResultDto>().ProducesValidationProblem();
    }

    private static void MapContracts(RouteGroupBuilder api)
    {
        var group = api.MapGroup("/contracts");

        group.MapGet("/", async ([AsParameters] ListContractsQuery query, ListContractsHandler handler, CancellationToken ct) => (await handler.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListContracts").Produces<PagedResult<ContractSummaryDto>>();

        group.MapGet("/expiring", async ([AsParameters] ExpiringQuery query, ExpiringContractsHandler handler, CancellationToken ct) => (await handler.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ExpiringContracts").Produces<PagedResult<ContractSummaryDto>>();

        group.MapPost("/", async (SaveContractRequest body, CreateContractHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToCreatedResult(c => $"/api/v1/contracts/{c.Summary.Id}"))
            .WithValidation<SaveContractRequest>().WithName("CreateContract").Produces<ContractDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, GetContractHandler handler, CancellationToken ct) => (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetContract").Produces<ContractDto>().ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, SaveContractRequest body, UpdateContractHandler handler, CancellationToken ct) => (await handler.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveContractRequest>().WithName("UpdateContract").Produces<ContractDto>().ProducesValidationProblem();

        group.MapGet("/{id:guid}/rates", async (Guid id, GetRatesHandler handler, CancellationToken ct) => (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("GetContractRates").Produces<IReadOnlyList<RateCardDto>>();

        group.MapPut("/{id:guid}/rates", async (Guid id, SaveRatesRequest body, ReplaceRatesHandler handler, CancellationToken ct) => (await handler.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveRatesRequest>().WithName("ReplaceContractRates").Produces<ContractDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/submit", async (Guid id, SubmitContractHandler handler, CancellationToken ct) => (await handler.HandleAsync(id, ct)).ToHttpResult())
            .WithName("SubmitContract").Produces<ContractDto>().ProducesValidationProblem();

        group.MapPost("/{id:guid}/terminate", async (Guid id, TerminateRequest body, TerminateContractHandler handler, CancellationToken ct) => (await handler.HandleAsync(id, body, ct)).ToHttpResult())
            .WithValidation<TerminateRequest>().WithName("TerminateContract").Produces<ContractDto>();

        group.MapPost("/{id:guid}/revise", async (Guid id, ReviseRequest body, ReviseContractHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, body, ct)).ToCreatedResult(c => $"/api/v1/contracts/{c.Summary.Id}"))
            .WithName("ReviseContract").Produces<ContractDto>(StatusCodes.Status201Created);
    }

    private static void MapMasters(RouteGroupBuilder api)
    {
        api.MapGet("/zones", async (ListZonesHandler handler, CancellationToken ct) => (await handler.HandleAsync(ct)).ToHttpResult())
            .WithName("ListZones").Produces<IReadOnlyList<ZoneDto>>();
        api.MapPost("/zones", async (SaveZoneRequest body, SaveZoneHandler handler, CancellationToken ct) =>
                (await handler.CreateAsync(body, ct)).ToCreatedResult(z => $"/api/v1/zones/{z.Id}"))
            .WithValidation<SaveZoneRequest>().WithName("CreateZone").Produces<ZoneDto>(StatusCodes.Status201Created);
        api.MapPut("/zones/{id:guid}", async (Guid id, SaveZoneRequest body, SaveZoneHandler handler, CancellationToken ct) => (await handler.UpdateAsync(id, body, ct)).ToHttpResult())
            .WithValidation<SaveZoneRequest>().WithName("UpdateZone").Produces<ZoneDto>();

        api.MapGet("/contracts/lookups/vehicle-types", async (ListVehicleTypesHandler handler, CancellationToken ct) => (await handler.HandleAsync(ct)).ToHttpResult())
            .WithName("ContractVehicleTypes").Produces<IReadOnlyList<Tms.SharedKernel.Contracts.VehicleTypeInfo>>();

        api.MapGet("/diesel-prices", async ([AsParameters] ListDieselPricesQuery query, ListDieselPricesHandler handler, CancellationToken ct) => (await handler.HandleAsync(query, ct)).ToHttpResult())
            .WithName("ListDieselPrices").Produces<IReadOnlyList<DieselPriceDto>>();
        api.MapPost("/diesel-prices", async (AddDieselPriceRequest body, AddDieselPriceHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(body, ct)).ToCreatedResult(p => $"/api/v1/diesel-prices/{p.Id}"))
            .WithValidation<AddDieselPriceRequest>().WithName("AddDieselPrice").Produces<DieselPriceDto>(StatusCodes.Status201Created);
    }

    private static void MapDocuments(RouteGroupBuilder api)
    {
        api.MapGet("/contracts/{id:guid}/documents", async (Guid id, DocumentHandler handler, CancellationToken ct) => (await handler.ListAsync(id, ct)).ToHttpResult())
            .WithName("ListContractDocuments").Produces<IReadOnlyList<ContractDocumentDto>>();

        api.MapPost("/contracts/{id:guid}/documents", async (Guid id, [FromForm] UploadContractDocumentForm form, DocumentHandler handler, CancellationToken ct) =>
                (await handler.UploadAsync(id, form, ct)).ToCreatedResult(d => $"/api/v1/contract-documents/{d.Id}"))
            .DisableAntiforgery() // bearer-token API: there is no cookie session for a CSRF attack to ride on
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024))
            .WithName("UploadContractDocument").Accepts<UploadContractDocumentForm>("multipart/form-data")
            .Produces<ContractDocumentDto>(StatusCodes.Status201Created);

        api.MapGet("/contract-documents/{documentId:guid}/file", async (Guid documentId, DocumentHandler handler, CancellationToken ct) =>
            {
                var result = await handler.DownloadAsync(documentId, ct);
                return result.IsSuccess ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.Error.ToProblem();
            })
            .WithName("DownloadContractDocument").Produces(StatusCodes.Status200OK);

        api.MapDelete("/contract-documents/{documentId:guid}", async (Guid documentId, DocumentHandler handler, CancellationToken ct) => (await handler.DeleteAsync(documentId, ct)).ToHttpResult())
            .WithName("DeleteContractDocument").Produces(StatusCodes.Status204NoContent);
    }
}
