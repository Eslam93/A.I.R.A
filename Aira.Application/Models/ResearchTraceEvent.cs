using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed record ResearchTraceEvent(
    WorkflowStep Step,
    TraceStatus Status,
    string Summary,
    IReadOnlyList<string> SourceIds,
    DateTimeOffset TimestampUtc);
