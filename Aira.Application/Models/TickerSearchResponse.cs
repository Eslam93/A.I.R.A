namespace Aira.Application.Models;

public sealed record TickerSearchResponse(
    IReadOnlyList<TickerSearchItemResponse> Items);
