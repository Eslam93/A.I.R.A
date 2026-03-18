using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed record ResearchJobRecord(
    Guid JobId,
    string Identifier,
    JobStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string CurrentStep,
    IReadOnlyList<string> Warnings,
    ResearchJobError? Error,
    string CorrelationId,
    IReadOnlyList<ResearchTraceEvent> Trace,
    CompletedResearchResult? Result,
    ResearchMode ResearchMode,
    AgentRole ActiveAgent,
    string AgentPhase)
{
    public ResearchJobRecord(
        Guid jobId,
        string identifier,
        JobStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc,
        string currentStep,
        IReadOnlyList<string> warnings,
        ResearchJobError? error,
        string correlationId,
        IReadOnlyList<ResearchTraceEvent> trace,
        CompletedResearchResult? result)
        : this(
            jobId,
            identifier,
            status,
            createdAtUtc,
            startedAtUtc,
            completedAtUtc,
            currentStep,
            warnings,
            error,
            correlationId,
            trace,
            result,
            ResearchMode.Standard,
            AgentRole.None,
            string.Empty)
    {
    }
}
