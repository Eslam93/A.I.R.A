using Aira.Domain.Enums;
using Aira.Domain.Models;

namespace Aira.Application.Models;

public sealed class AutonomousJobSession
{
    public AutonomousJobSession(
        Guid jobId,
        string identifier,
        string correlationId,
        ResearchExecutionMode executionMode,
        ResolvedTickerInfo resolvedTicker)
    {
        JobId = jobId;
        Identifier = identifier;
        CorrelationId = correlationId;
        ExecutionMode = executionMode;
        ResolvedTicker = resolvedTicker;
    }

    public Guid JobId { get; }

    public string Identifier { get; }

    public string CorrelationId { get; }

    public ResearchExecutionMode ExecutionMode { get; }

    public ResolvedTickerInfo ResolvedTicker { get; }

    public CoordinatorPlan? Plan { get; set; }

    public EvidenceGapReview? LatestEvidenceGapReview { get; set; }

    public HandoffPacket? HandoffPacket { get; set; }

    public AutonomousSourceResult<CompanyProfile> CompanyProfile { get; set; } = AutonomousSourceResult<CompanyProfile>.NotAttempted(SourceType.CompanyProfile);

    public AutonomousSourceResult<MarketSnapshot> MarketSnapshot { get; set; } = AutonomousSourceResult<MarketSnapshot>.NotAttempted(SourceType.MarketSnapshot);

    public AutonomousSourceResult<FinancialStatement> FinancialStatement { get; set; } = AutonomousSourceResult<FinancialStatement>.NotAttempted(SourceType.FinancialStatement);

    public AutonomousSourceResult<NewsCoverage> NewsCoverage { get; set; } = AutonomousSourceResult<NewsCoverage>.NotAttempted(SourceType.News);

    public AutonomousSourceResult<FilingCoverage> FilingCoverage { get; set; } = AutonomousSourceResult<FilingCoverage>.NotAttempted(SourceType.FilingExcerpt);

    public List<string> Warnings { get; } = [];

    public List<ResearchTraceEvent> Trace { get; } = [];

    public List<string> ToolHistory { get; } = [];

    public int ToolCallCount { get; set; }

    public int ReflectionRounds { get; set; }

    public int ValidationRetries { get; set; }

    public string? CoordinatorModel { get; set; }

    public string? SpecialistModel { get; set; }

    public bool HandoffOccurred { get; set; }
}
