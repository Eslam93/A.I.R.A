namespace Proofsheet.Application.Models;

public sealed record CoordinatorPlan(
    IReadOnlyList<string> PlannedTools,
    string Summary);
