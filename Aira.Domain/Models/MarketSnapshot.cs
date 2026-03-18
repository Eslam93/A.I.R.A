using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record MarketSnapshot(
    string Identifier,
    string SourceId,
    decimal PriceUsd,
    decimal OneYearReturnPercent,
    string MomentumSummary,
    string RiskSummary,
    string Title,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore);
