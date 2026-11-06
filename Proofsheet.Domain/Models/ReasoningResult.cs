using Proofsheet.Domain.Enums;

namespace Proofsheet.Domain.Models;

public sealed record ReasoningResult(
    string Company,
    string Thesis,
    InvestmentSignal Signal,
    int Confidence,
    IReadOnlyList<string> Insights,
    IReadOnlyList<InsightReference> InsightDetails,
    ThesisReference ThesisDetails,
    SignalReference SignalDetails,
    ConfidenceReference ConfidenceDetails,
    SynthesisMetadata SynthesisDetails);
