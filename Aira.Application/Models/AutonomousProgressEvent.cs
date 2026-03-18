using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed record AutonomousProgressEvent(
    WorkflowStep Step,
    AgentRole ActiveAgent,
    string AgentPhase,
    TraceStatus Status,
    string Summary,
    IReadOnlyList<string>? SourceIds = null);
