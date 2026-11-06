using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Models;

public sealed record SignalDetailResponse(
    InvestmentSignal Signal,
    string Rationale,
    IReadOnlyList<string> SourceIds,
    bool OverrideApplied);
