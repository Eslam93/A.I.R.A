using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record NewsCoverage(
    string Identifier,
    string Title,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore,
    IReadOnlyList<NewsItem> Items);
