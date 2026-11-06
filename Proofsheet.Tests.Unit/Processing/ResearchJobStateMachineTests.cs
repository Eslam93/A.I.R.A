using Proofsheet.Application.Models;
using Proofsheet.Application.Services;
using Proofsheet.Domain.Enums;

namespace Proofsheet.Tests.Unit.Processing;

public sealed class ResearchJobStateMachineTests
{
    [Fact]
    public void IsValidTransition_Allows_Queued_To_Running()
    {
        Assert.True(ResearchJobStateMachine.IsValidTransition(JobStatus.Queued, JobStatus.Running));
    }

    [Fact]
    public void MarkCompleted_Throws_For_Invalid_Transition()
    {
        var record = new ResearchJobRecord(
            Guid.NewGuid(),
            "AAPL",
            JobStatus.Queued,
            DateTimeOffset.UtcNow,
            null,
            null,
            JobStatus.Queued.ToString(),
            Array.Empty<string>(),
            null,
            Guid.NewGuid().ToString("N"),
            Array.Empty<ResearchTraceEvent>(),
            null);

        Assert.Throws<InvalidOperationException>(() =>
            ResearchJobStateMachine.MarkCompleted(
                record,
                DateTimeOffset.UtcNow,
                new CompletedResearchResult("Apple Inc.", "AAPL", "t", InvestmentSignal.Bullish, 90, DateTimeOffset.UtcNow, ["i1", "i2"], ["financials:AAPL"], Array.Empty<string>(), [new ResearchTraceEvent(WorkflowStep.Completion, TraceStatus.Completed, "done", Array.Empty<string>(), DateTimeOffset.UtcNow)], [new SourceDetailResponse("financials:AAPL", SourceType.FinancialStatement, "Financials", DateTimeOffset.UtcNow, TimestampType.AsOf, 0.9m, null, ProviderOrigin.Mock, "SeededMock", false)], [new ProviderUsageResponse(SourceType.FinancialStatement, ProviderResultStatus.Success, ProviderOrigin.Mock, "SeededMock", false, null)], [new InsightDetailResponse("i1", ["financials:AAPL"]), new InsightDetailResponse("i2", ["financials:AAPL"])], new ThesisDetailResponse("summary", ["financials:AAPL", "profile:AAPL"])),
                Array.Empty<string>(),
                Array.Empty<ResearchTraceEvent>()));
    }

    [Fact]
    public void UpdateProgress_Stores_Agent_Metadata()
    {
        var record = new ResearchJobRecord(
            Guid.NewGuid(),
            "AAPL",
            JobStatus.Running,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            WorkflowStep.IdentifierResolution.ToString(),
            Array.Empty<string>(),
            null,
            Guid.NewGuid().ToString("N"),
            Array.Empty<ResearchTraceEvent>(),
            null,
            ResearchMode.Autonomous,
            AgentRole.Coordinator,
            "Planning");

        var updated = ResearchJobStateMachine.UpdateProgress(
            record,
            WorkflowStep.SpecialistDraft.ToString(),
            AgentRole.Specialist,
            "SpecialistSynthesis",
            warnings: ["warn"],
            trace: [new ResearchTraceEvent(WorkflowStep.SpecialistDraft, TraceStatus.Completed, "done", Array.Empty<string>(), DateTimeOffset.UtcNow)]);

        Assert.Equal(AgentRole.Specialist, updated.ActiveAgent);
        Assert.Equal("SpecialistSynthesis", updated.AgentPhase);
        Assert.Equal(WorkflowStep.SpecialistDraft.ToString(), updated.CurrentStep);
    }
}
