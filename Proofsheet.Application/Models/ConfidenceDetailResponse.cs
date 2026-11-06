namespace Proofsheet.Application.Models;

public sealed record ConfidenceDetailResponse(
    string Rationale,
    int BoundedFrom,
    int BoundedTo,
    IReadOnlyList<string> SourceIds);
