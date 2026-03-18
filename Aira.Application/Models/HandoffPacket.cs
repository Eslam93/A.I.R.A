using Aira.Domain.Models;

namespace Aira.Application.Models;

public sealed record HandoffPacket(
    string Identifier,
    ResolvedTickerInfo Company,
    ResearchEvidence Evidence,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ProviderUsageResponse> ProviderUsage,
    string CoordinatorPlanSummary,
    IReadOnlyList<string> UnresolvedQuestions,
    IReadOnlyList<string> MissingSourceNotes);
