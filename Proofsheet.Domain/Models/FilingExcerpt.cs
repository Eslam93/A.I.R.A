using Proofsheet.Domain.Enums;

namespace Proofsheet.Domain.Models;

public sealed record FilingExcerpt(
    string SourceId,
    string Title,
    string Summary,
    string? Url,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore);
