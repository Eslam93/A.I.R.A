using Aira.Domain.Contracts;
using Aira.Domain.Enums;
using Aira.Domain.Models;
using Aira.Domain.Services;

namespace Aira.Infrastructure.Reasoning;

public sealed class DeterministicResearchReasoner : IResearchReasoner
{
    public Task<ReasoningResult> SynthesizeAsync(ResearchEvidence evidence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var signal = DeterministicScoringCalculator.DetermineSignal(evidence.Assessment);
        var thesis = BuildThesis(evidence, signal);
        var insightDetails = BuildInsights(evidence, signal);
        var insights = insightDetails.Select(detail => detail.Insight).ToArray();
        var thesisReference = BuildThesisReference(evidence, signal);
        var supportCoverageScore = DeterministicScoringCalculator.CalculateSupportCoverageScore(
            thesisReference,
            insightDetails,
            evidence.SourceCatalog.Keys.ToArray());
        var confidence = DeterministicScoringCalculator.CalculateConfidence(evidence.Assessment, supportCoverageScore);

        return Task.FromResult(
            new ReasoningResult(
                evidence.CompanyProfile.CompanyName,
                thesis,
                signal,
                confidence,
                insights,
                insightDetails,
                thesisReference,
                BuildSignalReference(evidence, signal),
                BuildConfidenceReference(evidence, confidence, supportCoverageScore),
                new SynthesisMetadata(evidence.ExecutionMode, "deterministic", 0, false)));
    }

    private static string BuildThesis(ResearchEvidence evidence, InvestmentSignal signal) =>
        signal switch
        {
            InvestmentSignal.Bullish => $"{evidence.CompanyProfile.CompanyName} combines {evidence.FinancialStatement.EfficiencySummary} with {BuildSupportClause(evidence)}, while {BuildRiskClause(evidence)} remains the main offset.",
            InvestmentSignal.Bearish => $"{evidence.CompanyProfile.CompanyName} faces deteriorating support from fundamentals and narrative evidence, with {BuildRiskClause(evidence)} outweighing the more positive evidence in the current setup.",
            _ => $"{evidence.CompanyProfile.CompanyName} presents mixed evidence: {BuildSupportClause(evidence)} remains relevant, but {BuildRiskClause(evidence)} and weaker directional confirmation keep conviction restrained."
        };

    private static IReadOnlyList<InsightReference> BuildInsights(ResearchEvidence evidence, InvestmentSignal signal)
    {
        var insights = new List<InsightReference>
        {
            BuildFinancialInsight(evidence),
            BuildNarrativeInsight(evidence),
            BuildRiskInsight(evidence, signal)
        };

        return insights
            .Where(x => !string.IsNullOrWhiteSpace(x.Insight))
            .Take(5)
            .ToArray();
    }

    private static InsightReference BuildFinancialInsight(ResearchEvidence evidence)
    {
        var text = evidence.FinancialStatement.StanceScore switch
        {
            >= 0.50m => $"{evidence.CompanyProfile.CompanyName} is converting revenue into strong cash generation and operating leverage.",
            <= -0.50m => $"{evidence.CompanyProfile.CompanyName} is showing pressure in margin conversion and cash generation.",
            _ => $"{evidence.CompanyProfile.CompanyName} shows stable but not clearly accelerating profitability."
        };

        return new InsightReference(text, [evidence.FinancialStatement.SourceId]);
    }

    private static InsightReference BuildNarrativeInsight(ResearchEvidence evidence)
    {
        var sourceIds = new List<string>();
        string text;

        if (evidence.NewsCoverage is not null && evidence.NewsCoverage.Items.Count > 0)
        {
            var leadNews = evidence.NewsCoverage.Items.OrderByDescending(x => x.QualityScore).First();
            sourceIds.Add(leadNews.SourceId);
            text = evidence.NewsCoverage.StanceScore switch
            {
                >= 0.25m => $"Recent coverage remains constructive, led by themes such as {leadNews.Headline.ToLowerInvariant()}.",
                <= -0.25m => $"Recent coverage leans cautious, reinforcing near-term execution concerns highlighted by {leadNews.Headline.ToLowerInvariant()}.",
                _ => "Recent coverage is mixed, which limits conviction in a stronger directional call."
            };
        }
        else if (evidence.FilingCoverage is not null && evidence.FilingCoverage.Items.Count > 0)
        {
            var filing = evidence.FilingCoverage.Items[0];
            sourceIds.Add(filing.SourceId);
            text = "Disclosed filing language remains an important anchor because narrative evidence is limited.";
        }
        else
        {
            sourceIds.Add(evidence.CompanyProfile.SourceId);
            text = "Narrative evidence is limited, so the profile-level business description carries more of the thesis.";
        }

        return new InsightReference(text, sourceIds);
    }

    private static InsightReference BuildRiskInsight(ResearchEvidence evidence, InvestmentSignal signal)
    {
        var sourceIds = new List<string> { evidence.CompanyProfile.SourceId };
        if (evidence.FilingCoverage?.Items.Count > 0)
        {
            sourceIds.Add(evidence.FilingCoverage.Items[0].SourceId);
        }
        else if (evidence.MarketSnapshot is not null)
        {
            sourceIds.Add(evidence.MarketSnapshot.SourceId);
        }
        else
        {
            sourceIds.Add(evidence.FinancialStatement.SourceId);
        }

        var text = evidence.Assessment.LowNewsMode
            ? "Sparse or low-quality news flow reduces narrative confidence, so the signal leans more heavily on fundamentals and filings."
            : evidence.Assessment.UnresolvedContradiction
                ? "Cross-source disagreement remains unresolved, which caps confidence despite available evidence."
                : signal == InvestmentSignal.Bullish
                    ? $"Primary risk remains {BuildRiskClause(evidence)}, but the current evidence still favors upside."
                    : signal == InvestmentSignal.Bearish
                        ? $"Primary risk around {BuildRiskClause(evidence)} is currently outweighing the business strengths."
                        : $"Primary risk remains {BuildRiskClause(evidence)}, which keeps the setup balanced rather than directional.";

        return new InsightReference(text, sourceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static ThesisReference BuildThesisReference(ResearchEvidence evidence, InvestmentSignal signal)
    {
        var sourceIds = new List<string> { evidence.FinancialStatement.SourceId, evidence.CompanyProfile.SourceId };
        if (evidence.FilingCoverage?.Items.Count > 0)
        {
            sourceIds.Add(evidence.FilingCoverage.Items[0].SourceId);
        }
        else if (evidence.MarketSnapshot is not null)
        {
            sourceIds.Add(evidence.MarketSnapshot.SourceId);
        }

        var summary = signal switch
        {
            InvestmentSignal.Bullish => "Thesis rests on strong financial execution, company positioning, and corroborating disclosures.",
            InvestmentSignal.Bearish => "Thesis rests on weakening fundamentals, risk disclosures, and limited confirmation from supporting sources.",
            _ => "Thesis rests on mixed fundamentals, company positioning, and conservative weighting of available disclosures."
        };

        if (summary.Length > 240)
        {
            summary = summary[..240];
        }

        return new ThesisReference(summary, sourceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static SignalReference BuildSignalReference(ResearchEvidence evidence, InvestmentSignal signal)
    {
        var sourceIds = BuildPrimarySourceIds(evidence);
        var rationale = evidence.Assessment.EvidenceScore < 55
            ? "The evidence score remained below the minimum threshold, so the signal is forced to Neutral."
            : signal switch
            {
                InvestmentSignal.Bullish => $"Positive directional evidence outweighed the risks across the strongest sources ({string.Join(", ", sourceIds)}).",
                InvestmentSignal.Bearish => $"Negative directional evidence outweighed the positives across the strongest sources ({string.Join(", ", sourceIds)}).",
                _ => $"Directional support stayed mixed or weak across the strongest sources ({string.Join(", ", sourceIds)})."
            };

        return new SignalReference(signal, rationale, sourceIds, evidence.Assessment.EvidenceScore < 55);
    }

    private static ConfidenceReference BuildConfidenceReference(ResearchEvidence evidence, int confidence, int supportCoverageScore)
    {
        var sourceIds = BuildPrimarySourceIds(evidence);
        var rationale = $"Confidence reflects completeness {evidence.Assessment.CompletenessScore}, evidence quality {evidence.Assessment.EvidenceScore}, agreement {evidence.Assessment.AgreementScore}, and support coverage {supportCoverageScore}.";
        return new ConfidenceReference(rationale, confidence, confidence, sourceIds);
    }

    private static IReadOnlyList<string> BuildPrimarySourceIds(ResearchEvidence evidence)
    {
        var sourceIds = new List<string>
        {
            evidence.FinancialStatement.SourceId,
            evidence.CompanyProfile.SourceId
        };

        if (evidence.FilingCoverage?.Items.Count > 0)
        {
            sourceIds.Add(evidence.FilingCoverage.Items[0].SourceId);
        }

        if (evidence.NewsCoverage?.Items.Count > 0)
        {
            sourceIds.Add(evidence.NewsCoverage.Items[0].SourceId);
        }

        return sourceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildSupportClause(ResearchEvidence evidence)
    {
        if (evidence.NewsCoverage?.Items.Count > 0)
        {
            return $"recent coverage remains anchored by {evidence.NewsCoverage.Items[0].Headline.ToLowerInvariant()}";
        }

        if (evidence.FilingCoverage?.Items.Count > 0)
        {
            return "recent filings continue to frame the business with current disclosures";
        }

        if (evidence.MarketSnapshot is not null)
        {
            return evidence.MarketSnapshot.MomentumSummary.ToLowerInvariant();
        }

        return "the available evidence remains anchored in the latest financial and company-profile data";
    }

    private static string BuildRiskClause(ResearchEvidence evidence)
    {
        if (evidence.MarketSnapshot is not null)
        {
            return TrimClause(evidence.MarketSnapshot.RiskSummary);
        }

        if (evidence.FilingCoverage?.Items.Count > 0)
        {
            return TrimClause(evidence.FilingCoverage.Items[0].Summary);
        }

        return "the available disclosures do not resolve the main downside risks cleanly";
    }

    private static string TrimClause(string text) =>
        text.Trim().TrimEnd('.').Trim();
}
