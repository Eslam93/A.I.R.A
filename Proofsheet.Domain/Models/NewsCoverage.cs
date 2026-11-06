using Proofsheet.Domain.Enums;

namespace Proofsheet.Domain.Models;

public sealed record NewsCoverage(
    string Identifier,
    string Title,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore,
    IReadOnlyList<NewsItem> Items);
