using Aira.Domain.Enums;
using Aira.Domain.Models;
using Aira.Domain.Services;
using Aira.Infrastructure.Providers;

namespace Aira.Tests.Unit;

internal static class TestEvidenceFactory
{
    public static ResearchEvidence Create(string identifier, int retryCount = 0, int missingNonCriticalSourceCount = 0, bool openAiFallbackOccurred = false)
    {
        var catalog = new MockResearchSeedCatalog();
        Assert.True(catalog.TryGet(identifier, out var seed));

        var scoringInput = new ResearchScoringInput(
            seed.CompanyProfile,
            seed.MarketSnapshot,
            seed.FinancialStatement,
            seed.NewsCoverage,
            seed.FilingCoverage,
            retryCount,
            missingNonCriticalSourceCount,
            openAiFallbackOccurred);

        var assessment = DeterministicScoringCalculator.Calculate(scoringInput);
        var sources = new Dictionary<string, SourceDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [seed.CompanyProfile.SourceId] = new(seed.CompanyProfile.SourceId, SourceType.CompanyProfile, seed.CompanyProfile.Title, seed.CompanyProfile.TimestampUtc, seed.CompanyProfile.TimestampType, seed.CompanyProfile.QualityScore, null, ProviderOrigin.Mock, "SeededMock", false),
            [seed.MarketSnapshot.SourceId] = new(seed.MarketSnapshot.SourceId, SourceType.MarketSnapshot, seed.MarketSnapshot.Title, seed.MarketSnapshot.TimestampUtc, seed.MarketSnapshot.TimestampType, seed.MarketSnapshot.QualityScore, null, ProviderOrigin.Mock, "SeededMock", false),
            [seed.FinancialStatement.SourceId] = new(seed.FinancialStatement.SourceId, SourceType.FinancialStatement, seed.FinancialStatement.Title, seed.FinancialStatement.TimestampUtc, seed.FinancialStatement.TimestampType, seed.FinancialStatement.QualityScore, null, ProviderOrigin.Mock, "SeededMock", false)
        };

        foreach (var item in seed.NewsCoverage.Items)
        {
            sources[item.SourceId] = new(item.SourceId, SourceType.News, item.Headline, item.TimestampUtc, item.TimestampType, item.QualityScore, item.Url, ProviderOrigin.Mock, "SeededMock", false);
        }

        foreach (var item in seed.FilingCoverage.Items)
        {
            sources[item.SourceId] = new(item.SourceId, SourceType.FilingExcerpt, item.Title, item.TimestampUtc, item.TimestampType, item.QualityScore, item.Url, ProviderOrigin.Mock, "SeededMock", false);
        }

        return new ResearchEvidence(
            identifier,
            seed.CompanyProfile,
            seed.MarketSnapshot,
            seed.FinancialStatement,
            seed.NewsCoverage,
            seed.FilingCoverage,
            assessment,
            Array.Empty<string>(),
            sources);
    }
}
