using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed class ResearchExecutionOptions
{
    public const string SectionName = "ResearchJobs";

    public int QueueCapacity { get; init; } = 100;

    public int MaxConcurrentJobs { get; init; } = 2;

    public int ProviderTimeoutMs { get; init; } = 2000;

    public int RetryDelayMs { get; init; } = 250;

    public int MaxRetries { get; init; } = 1;

    public int JobTimeoutMs { get; init; } = 90000;

    public int OverloadRetryAfterSeconds { get; init; } = 5;

    public ResearchExecutionMode Mode { get; init; } = ResearchExecutionMode.Auto;

    public int TickerSearchMinimumQueryLength { get; init; } = 2;

    public int TickerSearchDefaultLimit { get; init; } = 10;

    public int TickerSearchMaxLimit { get; init; } = 20;
}
