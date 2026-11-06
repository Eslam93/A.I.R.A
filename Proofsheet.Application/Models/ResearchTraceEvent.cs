using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Models;

public sealed record ResearchTraceEvent(
    WorkflowStep Step,
    TraceStatus Status,
    string Summary,
    IReadOnlyList<string> SourceIds,
    DateTimeOffset TimestampUtc);
