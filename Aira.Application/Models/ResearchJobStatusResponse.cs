using System.Text.Json.Serialization;
using Aira.Domain.Enums;

namespace Aira.Application.Models;

[method: JsonConstructor]
public sealed record ResearchJobStatusResponse(
    Guid JobId,
    string Identifier,
    JobStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string CurrentStep,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ResearchTraceEvent> Trace,
    ResearchJobError? Error,
    string CorrelationId,
    ResearchMode ResearchMode,
    AgentRole ActiveAgent,
    string AgentPhase)
{
    public ResearchJobStatusResponse(
        Guid jobId,
        string identifier,
        JobStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc,
        string currentStep,
        IReadOnlyList<string> warnings,
        IReadOnlyList<ResearchTraceEvent> trace,
        ResearchJobError? error,
        string correlationId)
        : this(
            jobId,
            identifier,
            status,
            createdAtUtc,
            startedAtUtc,
            completedAtUtc,
            currentStep,
            warnings,
            trace,
            error,
            correlationId,
            ResearchMode.Standard,
            AgentRole.None,
            string.Empty)
    {
    }
}
