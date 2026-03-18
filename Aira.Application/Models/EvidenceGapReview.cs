namespace Aira.Application.Models;

public sealed record EvidenceGapReview(
    bool ReadyForHandoff,
    IReadOnlyList<string> MissingRequiredDomains,
    IReadOnlyList<string> MissingAttemptDomains,
    IReadOnlyList<string> Concerns,
    string Summary);
