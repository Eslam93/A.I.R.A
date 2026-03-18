using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record SourceDocument(
    string SourceId,
    SourceType Type,
    string Title,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    string? Url,
    ProviderOrigin ProviderOrigin,
    string ProviderName,
    bool FallbackOccurred);
