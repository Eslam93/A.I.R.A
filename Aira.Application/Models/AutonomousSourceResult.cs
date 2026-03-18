using Aira.Domain.Enums;
using Aira.Domain.Models;

namespace Aira.Application.Models;

public sealed record AutonomousSourceResult<T>(
    SourceType SourceType,
    bool Attempted,
    ProviderResult<T>? Result,
    int RetryCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ResearchTraceEvent> TraceEvents)
{
    public static AutonomousSourceResult<T> NotAttempted(SourceType sourceType) =>
        new(sourceType, false, null, 0, Array.Empty<string>(), Array.Empty<ResearchTraceEvent>());

    public ProviderResultStatus Status => Result?.Status ?? ProviderResultStatus.Empty;

    public bool IsSuccess => Result?.Status == ProviderResultStatus.Success;

    public ProviderOrigin Origin => Result?.Origin ?? ProviderOrigin.Mock;

    public string ProviderName => Result?.ProviderName ?? "Unavailable";

    public bool FallbackOccurred => Result?.FallbackOccurred ?? false;

    public string? FallbackReason => Result?.FallbackReason;
}
