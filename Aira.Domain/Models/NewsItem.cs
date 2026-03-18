using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record NewsItem(
    string SourceId,
    string Headline,
    string Summary,
    string? Url,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore);
