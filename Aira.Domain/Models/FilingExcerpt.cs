using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record FilingExcerpt(
    string SourceId,
    string Title,
    string Summary,
    string? Url,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore);
