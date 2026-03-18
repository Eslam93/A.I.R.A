using System.Text.Json.Serialization;
using Aira.Domain.Enums;

namespace Aira.Application.Models;

[method: JsonConstructor]
public sealed record CompletedResearchResult(
    string Company,
    string Identifier,
    string Thesis,
    InvestmentSignal Signal,
    int Confidence,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<string> Insights,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ResearchTraceEvent> Trace,
    IReadOnlyList<SourceDetailResponse> SourceDetails,
    IReadOnlyList<ProviderUsageResponse> ProviderUsage,
    IReadOnlyList<InsightDetailResponse> InsightDetails,
    ThesisDetailResponse ThesisDetails,
    SignalDetailResponse SignalDetails,
    ConfidenceDetailResponse ConfidenceDetails,
    SynthesisDetailResponse SynthesisDetails,
    OrchestrationDetailResponse OrchestrationDetails)
{
    public CompletedResearchResult(
        string company,
        string identifier,
        string thesis,
        InvestmentSignal signal,
        int confidence,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<string> insights,
        IReadOnlyList<string> sources,
        IReadOnlyList<string> warnings,
        IReadOnlyList<ResearchTraceEvent> trace,
        IReadOnlyList<SourceDetailResponse> sourceDetails,
        IReadOnlyList<ProviderUsageResponse> providerUsage,
        IReadOnlyList<InsightDetailResponse> insightDetails,
        ThesisDetailResponse thesisDetails)
        : this(
            company,
            identifier,
            thesis,
            signal,
            confidence,
            generatedAtUtc,
            insights,
            sources,
            warnings,
            trace,
            sourceDetails,
            providerUsage,
            insightDetails,
            thesisDetails,
            new SignalDetailResponse(signal, "Signal rationale was not provided.", thesisDetails.SourceIds, false),
            new ConfidenceDetailResponse("Confidence rationale was not provided.", confidence, confidence, thesisDetails.SourceIds),
            new SynthesisDetailResponse(ResearchExecutionMode.Mock, "deterministic", 0, false),
            new OrchestrationDetailResponse(ResearchMode.Standard, null, null, false, 0, 0, 0))
    {
    }
}
