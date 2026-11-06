namespace Proofsheet.Application.Models;

public sealed record InsightDetailResponse(
    string Insight,
    IReadOnlyList<string> SourceIds);
