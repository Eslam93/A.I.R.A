using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record FilingCoverage(
    string Identifier,
    string Title,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore,
    IReadOnlyList<FilingExcerpt> Items);
