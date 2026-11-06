namespace Proofsheet.Application.Models;

public sealed record ThesisDetailResponse(
    string Summary,
    IReadOnlyList<string> SourceIds);
