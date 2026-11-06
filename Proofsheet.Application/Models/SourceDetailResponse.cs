using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Models;

public sealed record SourceDetailResponse(
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
