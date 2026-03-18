using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed record SignalDetailResponse(
    InvestmentSignal Signal,
    string Rationale,
    IReadOnlyList<string> SourceIds,
    bool OverrideApplied);
