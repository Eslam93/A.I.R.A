namespace Aira.Application.Models;

public sealed record ThesisDetailResponse(
    string Summary,
    IReadOnlyList<string> SourceIds);
