namespace Proofsheet.Domain.Models;

public sealed record ThesisReference(
    string Summary,
    IReadOnlyList<string> SourceIds);
