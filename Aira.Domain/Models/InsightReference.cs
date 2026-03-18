namespace Aira.Domain.Models;

public sealed record InsightReference(
    string Insight,
    IReadOnlyList<string> SourceIds);
