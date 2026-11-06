using Proofsheet.Domain.Enums;

namespace Proofsheet.Domain.Models;

public sealed record FinancialStatement(
    string Identifier,
    string SourceId,
    decimal RevenueGrowthPercent,
    decimal OperatingMarginPercent,
    decimal FreeCashFlowBillions,
    decimal CashBillions,
    decimal DebtBillions,
    string EfficiencySummary,
    string BalanceSheetSummary,
    string Title,
    DateTimeOffset TimestampUtc,
    TimestampType TimestampType,
    decimal QualityScore,
    decimal StanceScore);
