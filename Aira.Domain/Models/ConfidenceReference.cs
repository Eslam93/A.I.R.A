namespace Aira.Domain.Models;

public sealed record ConfidenceReference(
    string Rationale,
    int BoundedFrom,
    int BoundedTo,
    IReadOnlyList<string> SourceIds);
