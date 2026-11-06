using Proofsheet.Domain.Enums;

namespace Proofsheet.Domain.Models;

public sealed record NewsItem(
    string SourceId,
    string Headline,
    string Summary,
    string? Url,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore);
