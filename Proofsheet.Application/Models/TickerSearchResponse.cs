namespace Proofsheet.Application.Models;

public sealed record TickerSearchResponse(
    IReadOnlyList<TickerSearchItemResponse> Items);
