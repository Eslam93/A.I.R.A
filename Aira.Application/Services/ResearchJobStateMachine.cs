using Aira.Application.Models;
using Aira.Domain.Enums;

namespace Aira.Application.Services;

public static class ResearchJobStateMachine
{
    public static ResearchJobRecord MarkRunning(
        ResearchJobRecord record,
        DateTimeOffset startedAtUtc,
        string currentStep,
        AgentRole activeAgent = AgentRole.None,
        string agentPhase = "")
    {
        EnsureTransition(record.Status, JobStatus.Running);
        return record with
        {
            Status = JobStatus.Running,
            StartedAtUtc = startedAtUtc,
            CurrentStep = currentStep,
            ActiveAgent = activeAgent,
            AgentPhase = agentPhase ?? string.Empty
        };
    }

    public static ResearchJobRecord UpdateProgress(
        ResearchJobRecord record,
        string currentStep,
        AgentRole activeAgent = AgentRole.None,
        string agentPhase = "",
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<ResearchTraceEvent>? trace = null)
    {
        return record with
        {
            CurrentStep = currentStep,
            ActiveAgent = activeAgent,
            AgentPhase = agentPhase ?? string.Empty,
            Warnings = warnings ?? record.Warnings,
            Trace = trace ?? record.Trace
        };
    }

    public static ResearchJobRecord MarkCompleted(
        ResearchJobRecord record,
        DateTimeOffset completedAtUtc,
        CompletedResearchResult result,
        IReadOnlyList<string> warnings,
        IReadOnlyList<ResearchTraceEvent> trace)
    {
        EnsureTransition(record.Status, JobStatus.Completed);
        return record with
        {
            Status = JobStatus.Completed,
            CurrentStep = WorkflowStep.Completion.ToString(),
            CompletedAtUtc = completedAtUtc,
            Result = result,
            Warnings = warnings,
            Trace = trace,
            Error = null,
            ActiveAgent = AgentRole.None,
            AgentPhase = string.Empty
        };
    }

    public static ResearchJobRecord MarkFailed(
        ResearchJobRecord record,
        DateTimeOffset completedAtUtc,
        ResearchJobError error,
        IReadOnlyList<string> warnings,
        IReadOnlyList<ResearchTraceEvent> trace)
    {
        EnsureTransition(record.Status, JobStatus.Failed);
        return record with
        {
            Status = JobStatus.Failed,
            CurrentStep = JobStatus.Failed.ToString(),
            CompletedAtUtc = completedAtUtc,
            Error = error,
            Warnings = warnings,
            Trace = trace,
            ActiveAgent = AgentRole.None,
            AgentPhase = string.Empty
        };
    }

    public static bool IsValidTransition(JobStatus current, JobStatus next) =>
        current switch
        {
            JobStatus.Queued => next == JobStatus.Running,
            JobStatus.Running => next is JobStatus.Completed or JobStatus.Failed,
            _ => false
        };

    private static void EnsureTransition(JobStatus current, JobStatus next)
    {
        if (!IsValidTransition(current, next))
        {
            throw new InvalidOperationException($"Illegal transition from {current} to {next}.");
        }
    }
}
