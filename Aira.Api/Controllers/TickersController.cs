using Aira.Application.Abstractions;
using Aira.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aira.Api.Controllers;

[ApiController]
[Route("api/v1/tickers")]
public sealed class TickersController : ControllerBase
{
    private readonly ITickerDirectoryService _tickerDirectoryService;
    private readonly ResearchExecutionOptions _options;

    public TickersController(
        ITickerDirectoryService tickerDirectoryService,
        IOptions<ResearchExecutionOptions> options)
    {
        _tickerDirectoryService = tickerDirectoryService;
        _options = options.Value;
    }

    [HttpGet("search")]
    [ProducesResponseType(typeof(TickerSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TickerSearchResponse>> SearchAsync([FromQuery] string? q, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var query = q?.Trim() ?? string.Empty;
        var resolvedLimit = Math.Clamp(limit ?? _options.TickerSearchDefaultLimit, 1, _options.TickerSearchMaxLimit);
        if (query.Length < _options.TickerSearchMinimumQueryLength)
        {
            return Ok(new TickerSearchResponse(Array.Empty<TickerSearchItemResponse>()));
        }

        var items = await _tickerDirectoryService.SearchAsync(query, resolvedLimit, cancellationToken);
        return Ok(new TickerSearchResponse(items));
    }
}
