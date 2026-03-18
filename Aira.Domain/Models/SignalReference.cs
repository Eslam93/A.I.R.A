using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record SignalReference(
    InvestmentSignal Signal,
    string Rationale,
    IReadOnlyList<string> SourceIds,
    bool OverrideApplied);
