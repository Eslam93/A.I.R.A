namespace Proofsheet.Domain.Models;

public sealed record InsightReference(
    string Insight,
    IReadOnlyList<string> SourceIds);
