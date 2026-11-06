using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;
using Proofsheet.Domain.Services;
using Proofsheet.Infrastructure.Providers;
using Proofsheet.Infrastructure.Reasoning;

namespace Proofsheet.Tests.Unit.Scoring;

public sealed class DeterministicScoringCalculatorTests
{
    [Fact]
    public void Calculate_Uses_LowNews_Mode_For_TSLA()
    {
        var catalog = new MockResearchSeedCatalog();
        Assert.True(catalog.TryGet("TSLA", out var seed));

        var assessment = DeterministicScoringCalculator.Calculate(
            new ResearchScoringInput(
                seed.CompanyProfile,
                seed.MarketSnapshot,
                seed.FinancialStatement,
                seed.NewsCoverage,
                seed.FilingCoverage,
                RetryCount: 1,
                MissingNonCriticalSourceCount: 0,
                OpenAiFallbackOccurred: false));

        Assert.True(assessment.LowNewsMode);
        Assert.Equal(65, assessment.EvidenceScore);
        Assert.Equal(-3, assessment.DirectionalScore);
        Assert.Equal(InvestmentSignal.Neutral, DeterministicScoringCalculator.DetermineSignal(assessment));
    }

    [Fact]
    public async Task DeterministicReasoner_Returns_Bullish_For_Aapl()
    {
        var evidence = TestEvidenceFactory.Create("AAPL");
        var reasoner = new DeterministicResearchReasoner();

        var result = await reasoner.SynthesizeAsync(evidence, CancellationToken.None);

        Assert.Equal(InvestmentSignal.Bullish, result.Signal);
        Assert.InRange(result.Confidence, 80, 100);
        Assert.Equal(3, result.Insights.Count);
    }

    [Fact]
    public void CalculateConfidence_Applies_All_Penalties()
    {
        var assessment = new ResearchAssessment(
            LowNewsMode: true,
            ActiveWeights: new Dictionary<SourceType, decimal>(),
            CompletenessScore: 100,
            EvidenceScore: 80,
            DirectionalScore: 10,
            ContradictionScore: 0.35m,
            AgreementScore: 65,
            UnresolvedContradiction: true,
            RetryCount: 2,
            MissingNonCriticalSourceCount: 2,
            OpenAiFallbackOccurred: true);

        var confidence = DeterministicScoringCalculator.CalculateConfidence(assessment, 100);

        Assert.Equal(36, confidence);
    }
}
