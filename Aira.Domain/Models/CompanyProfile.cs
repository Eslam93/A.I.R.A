using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record CompanyProfile(
    string Identifier,
    string SourceId,
    string CompanyName,
    string? Exchange,
    string CikPadded,
    string Title,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore)
{
    public string? Sector => Exchange;
}
