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

public sealed class HybridNewsProvider : INewsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SupportedTimestampFormats = ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"];

    private readonly MockNewsProvider _mockProvider;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;
    private readonly HttpClient _httpClient;
    private readonly AlphaVantageOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HybridNewsProvider> _logger;

    [ActivatorUtilitiesConstructor]
    public HybridNewsProvider(
        MockNewsProvider mockProvider,
        IJobExecutionContextAccessor jobExecutionContextAccessor,
        HttpClient httpClient,
        IOptions<AlphaVantageOptions> options,
        TimeProvider timeProvider,
        ILogger<HybridNewsProvider> logger)
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

    public HybridNewsProvider(
        MockNewsProvider mockProvider,
        HttpClient httpClient,
        IOptions<AlphaVantageOptions> options,
        TimeProvider timeProvider,
        ILogger<HybridNewsProvider> logger)
        : this(mockProvider, CreateHybridContextAccessor(), httpClient, options, timeProvider, logger)
    {
    }

    public async Task<ProviderResult<NewsCoverage>> GetAsync(string identifier, CancellationToken cancellationToken)
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
                ? ProviderResult<NewsCoverage>.TransientFailure("Alpha Vantage news provider is disabled.", false, ProviderOrigin.Live, "Alpha Vantage")
                : fallback;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<NewsCoverage>.TransientFailure("Alpha Vantage was enabled but no API key was configured.", false, ProviderOrigin.Live, "Alpha Vantage")
                : ToMockFallback(fallback, "Alpha Vantage was enabled but no API key was configured.");
        }

        try
        {
            var path = $"query?function=NEWS_SENTIMENT&tickers={Uri.EscapeDataString(identifier)}&sort=LATEST&limit=5&apikey={Uri.EscapeDataString(_options.ApiKey)}";
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Alpha Vantage news request for {Identifier} returned {StatusCode}.", identifier, response.StatusCode);
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<NewsCoverage>.TransientFailure("Alpha Vantage returned a non-success response.", false, ProviderOrigin.Live, "Alpha Vantage")
                    : ToMockFallback(fallback, "Alpha Vantage returned a non-success response, so the seeded mock news coverage was used.");
            }

            // Since .NET 9, a JSON "null" body deserializes to a JsonDocument whose root is Null instead of to null.
            var payload = await response.Content.ReadFromJsonAsync<JsonDocument>(SerializerOptions, cancellationToken);
            if (payload is null || payload.RootElement.ValueKind == JsonValueKind.Null)
            {
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<NewsCoverage>.TransientFailure("Alpha Vantage returned an empty payload.", false, ProviderOrigin.Live, "Alpha Vantage")
                    : ToMockFallback(fallback, "Alpha Vantage returned an empty payload, so the seeded mock news coverage was used.");
            }

            var root = payload.RootElement;
            if (root.TryGetProperty("Note", out _) ||
                root.TryGetProperty("Information", out _) ||
                root.TryGetProperty("Error Message", out _))
            {
                _logger.LogWarning("Alpha Vantage news request for {Identifier} returned a throttled or error payload.", identifier);
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<NewsCoverage>.TransientFailure("Alpha Vantage returned a throttled or error payload.", false, ProviderOrigin.Live, "Alpha Vantage")
                    : ToMockFallback(fallback, "Alpha Vantage returned a throttled or error payload, so the seeded mock news coverage was used.");
            }

            if (!root.TryGetProperty("feed", out var feedElement) || feedElement.ValueKind != JsonValueKind.Array)
            {
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<NewsCoverage>.Empty(errorMessage: "Alpha Vantage did not return a usable feed.", origin: ProviderOrigin.Live, providerName: "Alpha Vantage")
                    : ToMockFallback(fallback, "Alpha Vantage did not return a usable feed, so the seeded mock news coverage was used.");
            }

            var items = new List<NewsItem>();
            var index = 1;
            foreach (var feedItem in feedElement.EnumerateArray())
            {
                var headline = TryGetString(feedItem, "title");
                if (string.IsNullOrWhiteSpace(headline))
                {
                    continue;
                }

                var summary = TryGetString(feedItem, "summary") ?? "Live Alpha Vantage news item.";
                var url = TryGetString(feedItem, "url");
                var timestamp = TryGetTimestamp(feedItem, _timeProvider.GetUtcNow());
                var relevance = TryResolveTickerMetric(feedItem, identifier, "relevance_score", 0.5m);
                var sentiment = TryResolveTickerMetric(feedItem, identifier, "ticker_sentiment_score", 0m);
                var quality = Math.Round(
                    Clamp(0.35m + (relevance * 0.45m) + (!string.IsNullOrWhiteSpace(summary) ? 0.10m : 0m), 0.30m, 0.90m),
                    2,
                    MidpointRounding.AwayFromZero);
                var stance = Math.Round(Clamp(sentiment, -1m, 1m), 2, MidpointRounding.AwayFromZero);
                items.Add(
                    new NewsItem(
                        $"news:{identifier}:{index:000}",
                        headline.Trim(),
                        summary.Trim(),
                        url,
                        timestamp,
                        TimestampType.Published,
                        quality,
                        stance));

                index++;
                if (items.Count == 3)
                {
                    break;
                }
            }

            if (items.Count == 0)
            {
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<NewsCoverage>.Empty(errorMessage: "Alpha Vantage did not return usable ticker-matched news items.", origin: ProviderOrigin.Live, providerName: "Alpha Vantage")
                    : ToMockFallback(fallback, "Alpha Vantage did not return usable ticker-matched news items, so the seeded mock news coverage was used.");
            }

            var coverageQuality = Math.Round(items.Average(item => item.QualityScore), 2, MidpointRounding.AwayFromZero);
            var coverageStance = Math.Round(items.Average(item => item.StanceScore), 2, MidpointRounding.AwayFromZero);
            var latestTimestamp = items.MaxBy(item => item.TimestampUtc)?.TimestampUtc ?? _timeProvider.GetUtcNow();

            return ProviderResult<NewsCoverage>.Success(
                new NewsCoverage(
                    identifier,
                    $"{identifier} news coverage",
                    latestTimestamp,
                    TimestampType.Published,
                    coverageQuality,
                    coverageStance,
                    items),
                ProviderOrigin.Live,
                "Alpha Vantage");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to mock news coverage for {Identifier}.", identifier);
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<NewsCoverage>.TransientFailure("Alpha Vantage news retrieval failed.", false, ProviderOrigin.Live, "Alpha Vantage")
                : ToMockFallback(fallback, "Alpha Vantage news retrieval failed, so the seeded mock news coverage was used.");
        }
    }

    private static ProviderResult<NewsCoverage> ToMockFallback(ProviderResult<NewsCoverage> fallback, string reason) =>
        ProviderResult<NewsCoverage>.Success(
            fallback.Value!,
            fallback.Origin,
            fallback.ProviderName,
            true,
            reason);

    private static decimal TryResolveTickerMetric(JsonElement feedItem, string identifier, string metricName, decimal fallback)
    {
        if (feedItem.TryGetProperty("ticker_sentiment", out var tickerSentimentElement) &&
            tickerSentimentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in tickerSentimentElement.EnumerateArray())
            {
                var ticker = TryGetString(item, "ticker");
                if (!string.Equals(ticker, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryGetDecimal(item, metricName, out var value))
                {
                    return value;
                }
            }
        }

        return metricName == "ticker_sentiment_score" && TryGetDecimal(feedItem, "overall_sentiment_score", out var overall)
            ? overall
            : fallback;
    }

    private static DateTimeOffset TryGetTimestamp(JsonElement element, DateTimeOffset fallback)
    {
        var raw = TryGetString(element, "time_published");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (DateTimeOffset.TryParseExact(
                raw,
                SupportedTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
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
