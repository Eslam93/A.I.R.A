using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed class ProviderResult<T>
{
    public required ProviderResultStatus Status { get; init; }

    public required ProviderOrigin Origin { get; init; }

    public required string ProviderName { get; init; }

    public T? Value { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsRetryable { get; init; }

    public bool FallbackOccurred { get; init; }

    public string? FallbackReason { get; init; }

    public static ProviderResult<T> Success(
        T value,
        ProviderOrigin origin,
        string providerName,
        bool fallbackOccurred = false,
        string? fallbackReason = null) =>
        new()
        {
            Status = ProviderResultStatus.Success,
            Origin = origin,
            ProviderName = providerName,
            Value = value,
            FallbackOccurred = fallbackOccurred,
            FallbackReason = fallbackReason
        };

    public static ProviderResult<T> Empty(
        bool isRetryable = false,
        string? errorMessage = null,
        ProviderOrigin origin = ProviderOrigin.Mock,
        string providerName = "SeededMock",
        bool fallbackOccurred = false,
        string? fallbackReason = null) =>
        new()
        {
            Status = ProviderResultStatus.Empty,
            Origin = origin,
            ProviderName = providerName,
            ErrorMessage = errorMessage,
            IsRetryable = isRetryable,
            FallbackOccurred = fallbackOccurred,
            FallbackReason = fallbackReason
        };

    public static ProviderResult<T> TransientFailure(
        string errorMessage,
        bool isRetryable = true,
        ProviderOrigin origin = ProviderOrigin.Mock,
        string providerName = "SeededMock",
        bool fallbackOccurred = false,
        string? fallbackReason = null) =>
        new()
        {
            Status = ProviderResultStatus.TransientFailure,
            Origin = origin,
            ProviderName = providerName,
            ErrorMessage = errorMessage,
            IsRetryable = isRetryable,
            FallbackOccurred = fallbackOccurred,
            FallbackReason = fallbackReason
        };
}
