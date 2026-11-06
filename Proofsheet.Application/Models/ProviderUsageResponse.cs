using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Models;

public sealed record ProviderUsageResponse(
    SourceType Domain,
    ProviderResultStatus Status,
    ProviderOrigin Origin,
    string ProviderName,
    bool FallbackOccurred,
    string? FallbackReason);
