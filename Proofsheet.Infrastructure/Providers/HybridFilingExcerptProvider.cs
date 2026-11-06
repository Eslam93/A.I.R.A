using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Application.Services;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;
using Proofsheet.Infrastructure.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Proofsheet.Infrastructure.Providers;

public sealed class HybridFilingExcerptProvider : IFilingExcerptProvider
{
    private static readonly string[] PreferredForms = ["10-K", "10-Q", "20-F", "40-F"];

    private readonly MockFilingExcerptProvider _mockProvider;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;
    private readonly SecEdgarClient _secEdgarClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HybridFilingExcerptProvider> _logger;

    [ActivatorUtilitiesConstructor]
    public HybridFilingExcerptProvider(
        MockFilingExcerptProvider mockProvider,
        IJobExecutionContextAccessor jobExecutionContextAccessor,
        SecEdgarClient secEdgarClient,
        TimeProvider timeProvider,
        ILogger<HybridFilingExcerptProvider> logger)
    {
        _mockProvider = mockProvider;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
        _secEdgarClient = secEdgarClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public HybridFilingExcerptProvider(
        MockFilingExcerptProvider mockProvider,
        SecEdgarClient secEdgarClient,
        TimeProvider timeProvider,
        ILogger<HybridFilingExcerptProvider> logger)
        : this(mockProvider, CreateHybridContextAccessor(), secEdgarClient, timeProvider, logger)
    {
    }

    public async Task<ProviderResult<FilingCoverage>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        var mode = _jobExecutionContextAccessor.Current?.ExecutionMode ?? ResearchExecutionMode.Mock;
        var fallback = await _mockProvider.GetAsync(identifier, cancellationToken);
        if (mode == ResearchExecutionMode.Mock)
        {
            return fallback;
        }

        if (!_secEdgarClient.Enabled)
        {
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<FilingCoverage>.TransientFailure("SEC EDGAR filing provider is disabled.", false, ProviderOrigin.Live, "SEC EDGAR")
                : fallback;
        }

        if (!_secEdgarClient.IsConfigured)
        {
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<FilingCoverage>.TransientFailure("SEC EDGAR was enabled but no valid User-Agent was configured.", false, ProviderOrigin.Live, "SEC EDGAR")
                : ToMockFallback(fallback, "SEC EDGAR was enabled but no valid User-Agent was configured.");
        }

        try
        {
            var submissions = await _secEdgarClient.GetSubmissionsAsync(identifier, cancellationToken);
            if (submissions is null || submissions.RecentFilings.Count == 0)
            {
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<FilingCoverage>.Empty(errorMessage: "SEC EDGAR did not return recent filings.", origin: ProviderOrigin.Live, providerName: "SEC EDGAR")
                    : ToMockFallback(fallback, "SEC EDGAR did not return recent filings, so the seeded mock filing coverage was used.");
            }

            var selectedFilings = submissions.RecentFilings
                .Where(filing => PreferredForms.Contains(filing.Form, StringComparer.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();

            if (selectedFilings.Length == 0)
            {
                selectedFilings = [submissions.RecentFilings[0]];
            }

            var items = selectedFilings
                .Select((filing, index) =>
                {
                    var qualityScore = filing.Form switch
                    {
                        "10-K" or "20-F" or "40-F" => 0.88m,
                        "10-Q" => 0.82m,
                        _ => 0.75m
                    };

                    return new FilingExcerpt(
                        $"filing:{identifier}:{index + 1:000}",
                        $"{filing.Form} filed {filing.FilingDate:yyyy-MM-dd}",
                        !string.IsNullOrWhiteSpace(filing.PrimaryDocDescription)
                            ? filing.PrimaryDocDescription!
                            : $"Recent {filing.Form} filing submitted to the SEC on {filing.FilingDate:yyyy-MM-dd}.",
                        filing.FilingUrl,
                        filing.FilingDate,
                        TimestampType.Published,
                        qualityScore,
                        0m);
                })
                .ToArray();

            var coverageQuality = Math.Round(items.Average(item => item.QualityScore), 2, MidpointRounding.AwayFromZero);
            var latestTimestamp = items.MaxBy(item => item.TimestampUtc)?.TimestampUtc ?? _timeProvider.GetUtcNow();
            return ProviderResult<FilingCoverage>.Success(
                new FilingCoverage(
                    identifier,
                    $"{submissions.CompanyName} filing coverage",
                    latestTimestamp,
                    TimestampType.Published,
                    coverageQuality,
                    0m,
                    items),
                ProviderOrigin.Live,
                "SEC EDGAR");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to mock filing coverage for {Identifier}.", identifier);
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<FilingCoverage>.TransientFailure("SEC EDGAR filing retrieval failed.", false, ProviderOrigin.Live, "SEC EDGAR")
                : ToMockFallback(fallback, "SEC EDGAR filing retrieval failed, so the seeded mock filing coverage was used.");
        }
    }

    private static ProviderResult<FilingCoverage> ToMockFallback(ProviderResult<FilingCoverage> fallback, string reason) =>
        ProviderResult<FilingCoverage>.Success(
            fallback.Value!,
            fallback.Origin,
            fallback.ProviderName,
            true,
            reason);

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
