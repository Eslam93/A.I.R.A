using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;

namespace Proofsheet.Domain.Services;

public static class DeterministicScoringCalculator
{
    private static readonly IReadOnlyDictionary<SourceType, decimal> BaselineWeights =
        new Dictionary<SourceType, decimal>
        {
            [SourceType.CompanyProfile] = 0.10m,
            [SourceType.MarketSnapshot] = 0.15m,
            [SourceType.FinancialStatement] = 0.30m,
            [SourceType.News] = 0.20m,
            [SourceType.FilingExcerpt] = 0.25m
        };

    private static readonly IReadOnlyDictionary<SourceType, decimal> LowNewsWeights =
        new Dictionary<SourceType, decimal>
        {
            [SourceType.CompanyProfile] = 0.10m,
            [SourceType.MarketSnapshot] = 0.15m,
            [SourceType.FinancialStatement] = 0.40m,
            [SourceType.News] = 0.10m,
            [SourceType.FilingExcerpt] = 0.25m
        };

    public static ResearchAssessment Calculate(ResearchScoringInput input)
    {
        var lowNewsMode = input.NewsCoverage is not null &&
            (input.NewsCoverage.QualityScore < 0.40m || input.NewsCoverage.Items.Count < 2);
        var weights = lowNewsMode ? LowNewsWeights : BaselineWeights;
        var metrics = BuildSourceMetrics(input);

        var completenessScore = RoundToInt(weights.Sum(x => x.Value * metrics[x.Key].Availability) * 100m);
        var evidenceScore = RoundToInt(weights.Sum(x => x.Value * metrics[x.Key].Availability * metrics[x.Key].QualityScore) * 100m);
        var directionalScore = RoundToInt(weights.Sum(x => x.Value * metrics[x.Key].Availability * metrics[x.Key].QualityScore * metrics[x.Key].StanceScore) * 100m);

        var contradictionScore = CalculateContradictionScore(weights, metrics);
        var agreementScore = RoundToInt((1 - contradictionScore) * 100m);
        var unresolvedContradiction = contradictionScore >= 0.30m && Math.Abs(directionalScore) < 20;

        return new ResearchAssessment(
            lowNewsMode,
            new Dictionary<SourceType, decimal>(weights),
            completenessScore,
            evidenceScore,
            directionalScore,
            contradictionScore,
            agreementScore,
            unresolvedContradiction,
            input.RetryCount,
            input.MissingNonCriticalSourceCount,
            input.OpenAiFallbackOccurred);
    }

    public static InvestmentSignal DetermineSignal(ResearchAssessment assessment)
    {
        if (assessment.EvidenceScore < 55)
        {
            return InvestmentSignal.Neutral;
        }

        if (assessment.DirectionalScore >= 20)
        {
            return InvestmentSignal.Bullish;
        }

        if (assessment.DirectionalScore <= -20)
        {
            return InvestmentSignal.Bearish;
        }

        return InvestmentSignal.Neutral;
    }

    public static int CalculateSupportCoverageScore(ThesisReference thesisDetails, IReadOnlyList<InsightReference> insightDetails, IReadOnlyCollection<string> availableSourceIds)
    {
        var totalClaims = insightDetails.Count + 1;
        var supportedClaims = 0;

        if (IsSupported(thesisDetails.SourceIds, availableSourceIds))
        {
            supportedClaims++;
        }

        supportedClaims += insightDetails.Count(detail => IsSupported(detail.SourceIds, availableSourceIds));

        return RoundToInt((decimal)supportedClaims / totalClaims * 100m);
    }

    public static int CalculateConfidence(ResearchAssessment assessment, int supportCoverageScore)
    {
        var baseConfidence = RoundToInt(
            0.30m * assessment.CompletenessScore +
            0.35m * assessment.EvidenceScore +
            0.20m * assessment.AgreementScore +
            0.15m * supportCoverageScore);

        var retryPenalty = Math.Min(assessment.RetryCount * 5, 10);
        var missingPenalty = Math.Min(assessment.MissingNonCriticalSourceCount * 10, 20);
        var contradictionPenalty = assessment.UnresolvedContradiction ? 15 : 0;
        var fallbackPenalty = assessment.OpenAiFallbackOccurred ? 5 : 0;
        var confidence = baseConfidence - retryPenalty - missingPenalty - contradictionPenalty - fallbackPenalty;

        return Math.Clamp(confidence, 0, 100);
    }

    private static bool IsSupported(IReadOnlyList<string> sourceIds, IReadOnlyCollection<string> availableSourceIds) =>
        sourceIds.Count > 0 && sourceIds.All(availableSourceIds.Contains);

    private static decimal CalculateContradictionScore(IReadOnlyDictionary<SourceType, decimal> weights, IReadOnlyDictionary<SourceType, SourceMetric> metrics)
    {
        var availableTypes = metrics
            .Where(x => x.Value.Availability == 1)
            .Select(x => x.Key)
            .ToArray();

        if (availableTypes.Length < 2)
        {
            return 0m;
        }

        decimal weightedSum = 0m;
        decimal weightSum = 0m;

        for (var i = 0; i < availableTypes.Length - 1; i++)
        {
            for (var j = i + 1; j < availableTypes.Length; j++)
            {
                var leftType = availableTypes[i];
                var rightType = availableTypes[j];
                var pairDisagreement = Math.Abs(metrics[leftType].StanceScore - metrics[rightType].StanceScore) / 2m;
                var pairWeight = weights[leftType] * weights[rightType];
                weightedSum += pairDisagreement * pairWeight;
                weightSum += pairWeight;
            }
        }

        if (weightSum == 0)
        {
            return 0m;
        }

        return Math.Round(weightedSum / weightSum, 2, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyDictionary<SourceType, SourceMetric> BuildSourceMetrics(ResearchScoringInput input) =>
        new Dictionary<SourceType, SourceMetric>
        {
            [SourceType.CompanyProfile] = SourceMetric.From(input.CompanyProfile?.QualityScore, input.CompanyProfile?.StanceScore),
            [SourceType.MarketSnapshot] = SourceMetric.From(input.MarketSnapshot?.QualityScore, input.MarketSnapshot?.StanceScore),
            [SourceType.FinancialStatement] = SourceMetric.From(input.FinancialStatement?.QualityScore, input.FinancialStatement?.StanceScore),
            [SourceType.News] = SourceMetric.From(input.NewsCoverage?.QualityScore, input.NewsCoverage?.StanceScore),
            [SourceType.FilingExcerpt] = SourceMetric.From(input.FilingCoverage?.QualityScore, input.FilingCoverage?.StanceScore)
        };

    private static int RoundToInt(decimal value) =>
        decimal.ToInt32(Math.Round(value, 0, MidpointRounding.AwayFromZero));

    private sealed record SourceMetric(decimal Availability, decimal QualityScore, decimal StanceScore)
    {
        public static SourceMetric From(decimal? qualityScore, decimal? stanceScore) =>
            qualityScore.HasValue && stanceScore.HasValue
                ? new SourceMetric(1m, qualityScore.Value, stanceScore.Value)
                : new SourceMetric(0m, 0m, 0m);
    }
}
