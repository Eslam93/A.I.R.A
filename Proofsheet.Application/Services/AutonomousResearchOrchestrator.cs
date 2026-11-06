using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Proofsheet.Application.Services;

public sealed class AutonomousResearchOrchestrator : IAutonomousResearchOrchestrator
{
    private readonly IResearchJobStore _jobStore;
    private readonly IAutonomousJobSessionStore _sessionStore;
    private readonly ICoordinatorAgentService _coordinatorAgentService;
    private readonly IStockReportSpecialistService _stockReportSpecialistService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutonomousResearchOrchestrator> _logger;

    public AutonomousResearchOrchestrator(
        IResearchJobStore jobStore,
        IAutonomousJobSessionStore sessionStore,
        ICoordinatorAgentService coordinatorAgentService,
        IStockReportSpecialistService stockReportSpecialistService,
        TimeProvider timeProvider,
        ILogger<AutonomousResearchOrchestrator> logger)
    {
        _jobStore = jobStore;
        _sessionStore = sessionStore;
        _coordinatorAgentService = coordinatorAgentService;
        _stockReportSpecialistService = stockReportSpecialistService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AutonomousResearchExecutionResult> ExecuteAsync(ResearchJobQueueItem item, CancellationToken cancellationToken)
    {
        var session = _sessionStore.Create(item);

        try
        {
            await ReportProgressAsync(
                session,
                new AutonomousProgressEvent(
                    WorkflowStep.IdentifierResolution,
                    AgentRole.Coordinator,
                    "Planning",
                    TraceStatus.Completed,
                    $"Identifier {item.Identifier} was normalized and resolved to {item.ResolvedTicker.CompanyName}."));

            await ReportProgressAsync(
                session,
                new AutonomousProgressEvent(
                    WorkflowStep.CoordinatorPlanning,
                    AgentRole.Coordinator,
                    "Planning",
                    TraceStatus.Completed,
                    "Coordinator started a dynamic bounded-tool planning pass."));

            var handoffPacket = await _coordinatorAgentService.ExecuteAsync(session, progress => ReportProgressAsync(session, progress), cancellationToken);
            var reasoningResult = await _stockReportSpecialistService.GenerateAsync(session, handoffPacket, progress => ReportProgressAsync(session, progress), cancellationToken);

            session.Trace.Add(
                new ResearchTraceEvent(
                    WorkflowStep.Completion,
                    TraceStatus.Completed,
                    $"Completed autonomous research job with signal {reasoningResult.Signal} and confidence {reasoningResult.Confidence}.",
                    Array.Empty<string>(),
                    _timeProvider.GetUtcNow()));

            UpdateRecord(
                item.JobId,
                record => ResearchJobStateMachine.UpdateProgress(
                    record,
                    WorkflowStep.Completion.ToString(),
                    AgentRole.None,
                    string.Empty,
                    session.Warnings.ToArray(),
                    session.Trace.ToArray()));

            var completedResult = BuildCompletedResult(session, handoffPacket, reasoningResult);
            return new AutonomousResearchExecutionResult(completedResult, session.Warnings.ToArray(), session.Trace.ToArray());
        }
        finally
        {
            _sessionStore.Remove(item.JobId);
        }
    }

    private Task ReportProgressAsync(AutonomousJobSession session, AutonomousProgressEvent progressEvent)
    {
        session.Trace.Add(
            new ResearchTraceEvent(
                progressEvent.Step,
                progressEvent.Status,
                progressEvent.Summary,
                progressEvent.SourceIds ?? Array.Empty<string>(),
                _timeProvider.GetUtcNow()));

        UpdateRecord(
            session.JobId,
            record => ResearchJobStateMachine.UpdateProgress(
                record,
                progressEvent.Step.ToString(),
                progressEvent.ActiveAgent,
                progressEvent.AgentPhase,
                session.Warnings.ToArray(),
                session.Trace.ToArray()));

        return Task.CompletedTask;
    }

    private CompletedResearchResult BuildCompletedResult(
        AutonomousJobSession session,
        HandoffPacket handoffPacket,
        ReasoningResult reasoningResult)
    {
        var citedSourceIds = reasoningResult.ThesisDetails.SourceIds
            .Concat(reasoningResult.InsightDetails.SelectMany(detail => detail.SourceIds))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sourceDetails = citedSourceIds
            .Select(sourceId => handoffPacket.Evidence.SourceCatalog[sourceId])
            .Select(document => new SourceDetailResponse(
                document.SourceId,
                document.Type,
                document.Title,
                document.TimestampUtc,
                document.TimestampType,
                Math.Round(document.QualityScore, 2, MidpointRounding.AwayFromZero),
                document.Url,
                document.ProviderOrigin,
                document.ProviderName,
                document.FallbackOccurred))
            .ToArray();

        var insightDetails = reasoningResult.InsightDetails
            .Select(detail => new InsightDetailResponse(detail.Insight, detail.SourceIds))
            .ToArray();

        return new CompletedResearchResult(
            reasoningResult.Company,
            session.Identifier,
            reasoningResult.Thesis,
            reasoningResult.Signal,
            reasoningResult.Confidence,
            _timeProvider.GetUtcNow(),
            reasoningResult.Insights,
            citedSourceIds,
            session.Warnings.ToArray(),
            session.Trace.ToArray(),
            sourceDetails,
            handoffPacket.ProviderUsage,
            insightDetails,
            new ThesisDetailResponse(reasoningResult.ThesisDetails.Summary, reasoningResult.ThesisDetails.SourceIds),
            new SignalDetailResponse(
                reasoningResult.SignalDetails.Signal,
                reasoningResult.SignalDetails.Rationale,
                reasoningResult.SignalDetails.SourceIds,
                reasoningResult.SignalDetails.OverrideApplied),
            new ConfidenceDetailResponse(
                reasoningResult.ConfidenceDetails.Rationale,
                reasoningResult.ConfidenceDetails.BoundedFrom,
                reasoningResult.ConfidenceDetails.BoundedTo,
                reasoningResult.ConfidenceDetails.SourceIds),
            new SynthesisDetailResponse(
                reasoningResult.SynthesisDetails.Mode,
                reasoningResult.SynthesisDetails.Model,
                reasoningResult.SynthesisDetails.ValidationRetries,
                reasoningResult.SynthesisDetails.FallbackOccurred),
            new OrchestrationDetailResponse(
                ResearchMode.Autonomous,
                session.CoordinatorModel,
                session.SpecialistModel,
                session.HandoffOccurred,
                session.ToolCallCount,
                session.ReflectionRounds,
                session.ValidationRetries));
    }

    private void UpdateRecord(Guid jobId, Func<ResearchJobRecord, ResearchJobRecord> update)
    {
        if (!_jobStore.TryUpdate(jobId, update))
        {
            _logger.LogWarning("Unable to update autonomous job record {JobId}.", jobId);
        }
    }
}
