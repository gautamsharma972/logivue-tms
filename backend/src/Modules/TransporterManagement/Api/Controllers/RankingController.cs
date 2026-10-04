using LogiVue.Tms.TransporterManagement.Api.Filters;
using LogiVue.Tms.TransporterManagement.Application.Ranking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

/// <summary>Transporter ranking and peer benchmarking from stored KPIs. Read-only for internal users.</summary>
[ApiController]
[Route("api/v1/transporters")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class RankingController(IRankingService rankings) : ControllerBase
{
    /// <summary>Ranks transporters on the chosen metric. Transporters below the minimum sample are listed unranked.</summary>
    [HttpGet("rankings")]
    [ProducesResponseType<RankingResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RankingResultDto>> Rank(
        [FromQuery] RankingScope scope,
        [FromQuery] RankingMetric metric = RankingMetric.OverallScore,
        CancellationToken cancellationToken = default) =>
        Ok(await rankings.RankAsync(scope, metric, cancellationToken));

    /// <summary>Compares one transporter with its lane, region and category averages and with the top performer.</summary>
    [HttpGet("benchmark")]
    [ProducesResponseType<BenchmarkDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BenchmarkDto>> Benchmark(
        [FromQuery] long transporterId,
        [FromQuery] RankingScope scope,
        CancellationToken cancellationToken) =>
        Ok(await rankings.BenchmarkAsync(transporterId, scope, cancellationToken));
}
