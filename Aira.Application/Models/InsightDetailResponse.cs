namespace Aira.Application.Models;

public sealed record InsightDetailResponse(
    string Insight,
    IReadOnlyList<string> SourceIds);
