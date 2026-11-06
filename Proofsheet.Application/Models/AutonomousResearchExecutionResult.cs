namespace Proofsheet.Application.Models;

public sealed record AutonomousResearchExecutionResult(
    CompletedResearchResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ResearchTraceEvent> Trace);
