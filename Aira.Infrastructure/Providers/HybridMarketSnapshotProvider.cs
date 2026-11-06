using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Application.Services;
using Aira.Domain.Contracts;
using Aira.Domain.Enums;
using Aira.Domain.Models;
using Aira.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aira.Infrastructure.Providers;

public sealed class HybridMarketSnapshotProvider : IMarketSnapshotProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly MockMarketSnapshotProvider _mockProvider;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;
    private readonly HttpClient _httpClient;
    private readonly TwelveDataOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HybridMarketSnapshotProvider> _logger;

    [ActivatorUtilitiesConstructor]
    public HybridMarketSnapshotProvider(
        MockMarketSnapshotProvider mockProvider,
        IJobExecutionContextAccessor jobExecutionContextAccessor,
        HttpClient httpClient,
        IOptions<TwelveDataOptions> options,
        TimeProvider timeProvider,
        ILogger<HybridMarketSnapshotProvider> logger)
    {
        _mockProvider = mockProvider;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
        _httpClient = httpClient;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(_options.BaseUrl));
        }
    }

    public HybridMarketSnapshotProvider(
        MockMarketSnapshotProvider mockProvider,
        HttpClient httpClient,
        IOptions<TwelveDataOptions> options,
        TimeProvider timeProvider,
        ILogger<HybridMarketSnapshotProvider> logger)
        : this(mockProvider, CreateHybridContextAccessor(), httpClient, options, timeProvider, logger)
    {
    }

    public async Task<ProviderResult<MarketSnapshot>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        var mode = _jobExecutionContextAccessor.Current?.ExecutionMode ?? ResearchExecutionMode.Mock;
        var fallback = await _mockProvider.GetAsync(identifier, cancellationToken);
        if (mode == ResearchExecutionMode.Mock)
        {
            return fallback;
        }

        if (!_options.Enabled)
        {
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<MarketSnapshot>.TransientFailure("Twelve Data market snapshot provider is disabled.", false, ProviderOrigin.Live, "Twelve Data")
                : fallback;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<MarketSnapshot>.TransientFailure("Twelve Data was enabled but no API key was configured.", false, ProviderOrigin.Live, "Twelve Data")
                : ToMockFallback(fallback, "Twelve Data was enabled but no API key was configured.");
        }

        try
        {
            var path = $"quote?symbol={Uri.EscapeDataString(identifier)}&apikey={Uri.EscapeDataString(_options.ApiKey)}";
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twelve Data quote request for {Identifier} returned {StatusCode}.", identifier, response.StatusCode);
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<MarketSnapshot>.TransientFailure("Twelve Data returned a non-success response.", false, ProviderOrigin.Live, "Twelve Data")
                    : ToMockFallback(fallback, "Twelve Data returned a non-success response, so the seeded mock snapshot was used.");
            }

            // Since .NET 9, a JSON "null" body deserializes to a JsonDocument whose root is Null instead of to null.
            var payload = await response.Content.ReadFromJsonAsync<JsonDocument>(SerializerOptions, cancellationToken);
            if (payload is null || payload.RootElement.ValueKind == JsonValueKind.Null)
            {
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<MarketSnapshot>.TransientFailure("Twelve Data returned an empty payload.", false, ProviderOrigin.Live, "Twelve Data")
                    : ToMockFallback(fallback, "Twelve Data returned an empty payload, so the seeded mock snapshot was used.");
            }

            var root = payload.RootElement;
            if (IsErrorPayload(root))
            {
                _logger.LogWarning("Twelve Data quote request for {Identifier} returned an error payload.", identifier);
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<MarketSnapshot>.TransientFailure("Twelve Data returned an error payload.", false, ProviderOrigin.Live, "Twelve Data")
                    : ToMockFallback(fallback, "Twelve Data returned an error payload, so the seeded mock snapshot was used.");
            }

            if (!TryGetDecimal(root, "close", out var price) &&
                !TryGetDecimal(root, "price", out price))
            {
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<MarketSnapshot>.Empty(errorMessage: "Twelve Data did not return a usable price.", origin: ProviderOrigin.Live, providerName: "Twelve Data")
                    : ToMockFallback(fallback, "Twelve Data did not return a usable price, so the seeded mock snapshot was used.");
            }

            TryGetDecimal(root, "percent_change", out var percentChange);
            var timestamp = TryGetTimestamp(root, _timeProvider.GetUtcNow());
            var qualityScore = Math.Round(
                Clamp(0.60m + (percentChange != 0m ? 0.10m : 0m) + (timestamp != default ? 0.10m : 0m), 0.55m, 0.85m),
                2,
                MidpointRounding.AwayFromZero);
            var stanceScore = Math.Round(Clamp(percentChange / 5m, -1m, 1m), 2, MidpointRounding.AwayFromZero);
            var momentumSummary = percentChange switch
            {
                >= 2m => "Live quote momentum is notably positive heading into the current session context.",
                > 0m => "Live quote momentum is modestly positive.",
                <= -2m => "Live quote momentum is notably negative in the current session context.",
                < 0m => "Live quote momentum is modestly negative.",
                _ => "Live quote momentum is broadly flat."
            };
            var riskSummary = Math.Abs(percentChange) >= 3m
                ? "Short-term price action is elevated, so quote-based direction should be treated cautiously."
                : "Short-term quote moves are informative but can reverse quickly around catalysts.";

            return ProviderResult<MarketSnapshot>.Success(
                new MarketSnapshot(
                    identifier,
                    $"market:{identifier}",
                    price,
                    percentChange,
                    momentumSummary,
                    riskSummary,
                    $"{identifier} market snapshot",
                    timestamp,
                    TimestampType.AsOf,
                    qualityScore,
                    stanceScore),
                ProviderOrigin.Live,
                "Twelve Data");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to mock market snapshot for {Identifier}.", identifier);
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<MarketSnapshot>.TransientFailure("Twelve Data market snapshot retrieval failed.", false, ProviderOrigin.Live, "Twelve Data")
                : ToMockFallback(fallback, "Twelve Data market snapshot retrieval failed, so the seeded mock snapshot was used.");
        }
    }

    private static ProviderResult<MarketSnapshot> ToMockFallback(ProviderResult<MarketSnapshot> fallback, string reason) =>
        ProviderResult<MarketSnapshot>.Success(
            fallback.Value!,
            fallback.Origin,
            fallback.ProviderName,
            true,
            reason);

    private static bool IsErrorPayload(JsonElement root) =>
        string.Equals(TryGetString(root, "status"), "error", StringComparison.OrdinalIgnoreCase) ||
        root.TryGetProperty("code", out _) ||
        (root.TryGetProperty("message", out var messageElement) &&
         messageElement.ValueKind == JsonValueKind.String &&
         !root.TryGetProperty("close", out _) &&
         !root.TryGetProperty("price", out _));

    private static DateTimeOffset TryGetTimestamp(JsonElement root, DateTimeOffset fallback)
    {
        var raw = TryGetString(root, "datetime") ?? TryGetString(root, "timestamp");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return fallback;
    }

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetDecimal(out value);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static decimal Clamp(decimal value, decimal minimum, decimal maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";

    private static IJobExecutionContextAccessor CreateHybridContextAccessor()
    {
        var accessor = new JobExecutionContextAccessor();
        accessor.Current = new JobExecutionContext
        {
            JobId = Guid.NewGuid(),
            Identifier = "TEST",
            CorrelationId = Guid.NewGuid().ToString("N"),
            ExecutionMode = ResearchExecutionMode.Hybrid,
            ResolvedTicker = new("TEST", "Test Company", "NASDAQ", "0000000000")
        };

        return accessor;
    }
}
