using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;
using Proofsheet.Infrastructure.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Application.Services;

namespace Proofsheet.Infrastructure.Providers;

public sealed class HybridFinancialStatementProvider : IFinancialStatementProvider
{
    // Revenue comes from whichever concept is still being reported; list order only breaks ties.
    private static readonly string[] RevenueConcepts =
    [
        "Revenues",
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "SalesRevenueNet"
    ];

    // For every other figure, the first concept that reports the revenue's fiscal year is used.
    private static readonly string[] OperatingIncomeConcepts =
    [
        "OperatingIncomeLoss"
    ];

    private static readonly string[] CashFromOperationsConcepts =
    [
        "NetCashProvidedByUsedInOperatingActivities",
        "NetCashProvidedByUsedInOperatingActivitiesContinuingOperations"
    ];

    // Cash paid for capital expenditure. PropertyPlantAndEquipmentAdditions and
    // CapitalExpendituresIncurredButNotYetPaid are left out on purpose: both include non-cash additions.
    private static readonly string[] CapitalExpenditureConcepts =
    [
        "PaymentsToAcquirePropertyPlantAndEquipment",
        "PaymentsToAcquireProductiveAssets"
    ];

    private static readonly string[] CashConcepts =
    [
        "CashAndCashEquivalentsAtCarryingValue",
        "CashCashEquivalentsRestrictedCashAndRestrictedCashEquivalents"
    ];

    // Most to least inclusive. LongTermDebt can leave out commercial paper and short-term borrowings.
    private static readonly string[] TotalDebtConcepts =
    [
        "DebtLongtermAndShorttermCombinedAmount",
        "LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities",
        "DebtAndCapitalLeaseObligations",
        "LongTermDebt"
    ];

    // DebtCurrent is the whole current part (current maturities plus commercial paper and other short-term debt).
    private static readonly string[] CurrentDebtConcepts =
    [
        "DebtCurrent",
        "LongTermDebtCurrent",
        "ShortTermBorrowings",
        "LongTermDebtAndCapitalLeaseObligationsCurrent"
    ];

    // Despite its name, LongTermDebtAndCapitalLeaseObligations is the non-current amount only.
    private static readonly string[] NonCurrentDebtConcepts =
    [
        "LongTermDebtNoncurrent",
        "LongTermDebtAndCapitalLeaseObligations"
    ];

    // A company's next 10-K is due within about three months of its fiscal year end, so a latest fiscal year more than
    // 18 months old means the company stopped filing or the facts are incomplete.
    private const int MaxFiscalYearAgeMonths = 18;

    private readonly MockFinancialStatementProvider _mockProvider;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;
    private readonly SecEdgarClient _secEdgarClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HybridFinancialStatementProvider> _logger;

    [ActivatorUtilitiesConstructor]
    public HybridFinancialStatementProvider(
        MockFinancialStatementProvider mockProvider,
        IJobExecutionContextAccessor jobExecutionContextAccessor,
        SecEdgarClient secEdgarClient,
        TimeProvider timeProvider,
        ILogger<HybridFinancialStatementProvider> logger)
    {
        _mockProvider = mockProvider;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
        _secEdgarClient = secEdgarClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public HybridFinancialStatementProvider(
        MockFinancialStatementProvider mockProvider,
        SecEdgarClient secEdgarClient,
        TimeProvider timeProvider,
        ILogger<HybridFinancialStatementProvider> logger)
        : this(mockProvider, CreateHybridContextAccessor(), secEdgarClient, timeProvider, logger)
    {
    }

    public async Task<ProviderResult<FinancialStatement>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        var mode = _jobExecutionContextAccessor.Current?.ExecutionMode ?? ResearchExecutionMode.Mock;
        var fallback = await _mockProvider.GetAsync(identifier, cancellationToken);
        if (mode == ResearchExecutionMode.Mock)
        {
            return fallback;
        }

        if (!_secEdgarClient.Enabled)
        {
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<FinancialStatement>.TransientFailure("SEC EDGAR financial statement provider is disabled.", false, ProviderOrigin.Live, "SEC EDGAR")
                : fallback;
        }

        if (!_secEdgarClient.IsConfigured)
        {
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<FinancialStatement>.TransientFailure("SEC EDGAR was enabled but no valid User-Agent was configured.", false, ProviderOrigin.Live, "SEC EDGAR")
                : ToMockFallback(fallback, "SEC EDGAR was enabled but no valid User-Agent was configured.");
        }

        try
        {
            var companyFacts = await _secEdgarClient.GetCompanyFactsAsync(identifier, cancellationToken);
            string? unavailableReason;
            FinancialStatement? statement = null;
            if (companyFacts is null)
            {
                unavailableReason = "no company facts were found";
            }
            else
            {
                TryBuildLiveStatement(identifier, companyFacts, _timeProvider.GetUtcNow(), out statement, out unavailableReason);
            }

            if (statement is null)
            {
                _logger.LogInformation("SEC EDGAR financial facts for {Identifier} were incomplete: {Reason}.", identifier, unavailableReason);
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<FinancialStatement>.Empty(errorMessage: $"SEC EDGAR financial facts were incomplete: {unavailableReason}.", origin: ProviderOrigin.Live, providerName: "SEC EDGAR")
                    : ToMockFallback(fallback, $"SEC EDGAR financial facts were incomplete ({unavailableReason}), so the seeded mock statements were used.");
            }

            return ProviderResult<FinancialStatement>.Success(statement, ProviderOrigin.Live, "SEC EDGAR");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falling back to mock financial statements for {Identifier}.", identifier);
            return mode == ResearchExecutionMode.LiveRequired
                ? ProviderResult<FinancialStatement>.TransientFailure("SEC EDGAR financial statement retrieval failed.", false, ProviderOrigin.Live, "SEC EDGAR")
                : ToMockFallback(fallback, "SEC EDGAR financial statement retrieval failed, so the seeded mock statements were used.");
        }
    }

    private static ProviderResult<FinancialStatement> ToMockFallback(ProviderResult<FinancialStatement> fallback, string reason) =>
        ProviderResult<FinancialStatement>.Success(
            fallback.Value!,
            fallback.Origin,
            fallback.ProviderName,
            true,
            reason);

    private static bool TryBuildLiveStatement(
        string identifier,
        SecCompanyFactsData companyFacts,
        DateTimeOffset now,
        [NotNullWhen(true)] out FinancialStatement? financialStatement,
        [NotNullWhen(false)] out string? unavailableReason)
    {
        financialStatement = null;
        var facts = companyFacts.CompanyFactsRoot;

        if (!SecFactSelector.TryGetLatestFiscalYearPair(facts, RevenueConcepts, SecFactSelector.Usd, out var revenue, out var previousRevenue))
        {
            unavailableReason = "no two consecutive fiscal years of us-gaap revenue in USD";
            return false;
        }

        var fiscalYearEnd = revenue.EndDate;
        var fiscalYearEndText = FormatDate(fiscalYearEnd);

        // When another figure already has a newer fiscal year, the newest revenue is tagged with a concept this provider
        // does not read, and the statement would silently describe the previous year.
        if (SecFactSelector.TryGetFiscalYearAfter(
                facts,
                [.. OperatingIncomeConcepts, .. CashFromOperationsConcepts, .. CapitalExpenditureConcepts],
                SecFactSelector.Usd,
                fiscalYearEnd,
                out var newerFiscalYearEnd))
        {
            unavailableReason = $"no revenue for the fiscal year ending {FormatDate(newerFiscalYearEnd)}; the newest revenue on file is for {fiscalYearEndText}";
            return false;
        }

        var staleCutoff = now.AddMonths(-MaxFiscalYearAgeMonths);
        if (fiscalYearEnd <= staleCutoff)
        {
            unavailableReason = $"the latest fiscal year ended on {fiscalYearEndText}, more than {MaxFiscalYearAgeMonths} months ago";
            return false;
        }

        if (revenue.Value == 0m)
        {
            unavailableReason = $"revenue for the fiscal year ending {fiscalYearEndText} is zero";
            return false;
        }

        // Every other figure has to describe the same fiscal year as revenue.
        var missing = new List<string>();
        var operatingIncome = SecFactSelector.TryGetFiscalYearFact(facts, OperatingIncomeConcepts, SecFactSelector.Usd, fiscalYearEnd, out var operatingIncomeFact)
            ? operatingIncomeFact
            : Missing("operating income");
        var operatingCashFlow = SecFactSelector.TryGetFiscalYearFact(facts, CashFromOperationsConcepts, SecFactSelector.Usd, fiscalYearEnd, out var operatingCashFlowFact)
            ? operatingCashFlowFact
            : Missing("operating cash flow");
        var capitalExpenditure = SecFactSelector.TryGetFiscalYearFact(facts, CapitalExpenditureConcepts, SecFactSelector.Usd, fiscalYearEnd, out var capitalExpenditureFact)
            ? capitalExpenditureFact
            : Missing("capital expenditure");
        // Balances come from annual reports first, so later 10-Qs that re-tag the year-end column cannot change them.
        var cash = SecFactSelector.TryGetInstantFact(facts, CashConcepts, SecFactSelector.Usd, fiscalYearEnd, annualReportsOnly: true, out var cashFact) ||
            SecFactSelector.TryGetInstantFact(facts, CashConcepts, SecFactSelector.Usd, fiscalYearEnd, annualReportsOnly: false, out cashFact)
            ? cashFact
            : Missing("cash");
        var debt = TryGetDebt(facts, fiscalYearEnd, annualReportsOnly: true, out var debtFact) ||
            TryGetDebt(facts, fiscalYearEnd, annualReportsOnly: false, out debtFact)
            ? debtFact
            : Missing("debt");

        if (operatingIncome is null || operatingCashFlow is null || capitalExpenditure is null || cash is null || debt is null)
        {
            unavailableReason = $"no {string.Join(", ", missing)} for the fiscal year ending {fiscalYearEndText}";
            return false;
        }

        var revenueGrowthPercent = previousRevenue.Value == 0m
            ? 0m
            : ((revenue.Value - previousRevenue.Value) / Math.Abs(previousRevenue.Value)) * 100m;
        var operatingMarginPercent = (operatingIncome.Value / revenue.Value) * 100m;
        var freeCashFlowBillions = (operatingCashFlow.Value - capitalExpenditure.Value) / 1_000_000_000m;
        var cashBillions = cash.Value / 1_000_000_000m;
        var debtBillions = debt.Value / 1_000_000_000m;

        var growthSignal = Clamp(revenueGrowthPercent / 20m, -1m, 1m);
        var marginSignal = Clamp((operatingMarginPercent - 10m) / 20m, -1m, 1m);
        var balanceSignal = Clamp((cashBillions - debtBillions) / Math.Max(Math.Abs(cashBillions) + Math.Abs(debtBillions), 1m), -1m, 1m);
        var stanceScore = Math.Round(Clamp((growthSignal * 0.40m) + (marginSignal * 0.40m) + (balanceSignal * 0.20m), -1m, 1m), 2, MidpointRounding.AwayFromZero);

        var qualityScore = Math.Round(
            Clamp(
                0.70m +
                (fiscalYearEnd > staleCutoff ? 0.10m : 0m) +
                (Math.Abs(revenueGrowthPercent) > 0m ? 0.05m : 0m),
                0.60m,
                0.90m),
            2,
            MidpointRounding.AwayFromZero);

        var efficiencySummary = operatingMarginPercent switch
        {
            >= 25m => "strong operating margins continue to support resilient cash generation",
            >= 10m => "positive operating margins support a credible efficiency profile",
            > 0m => "operating profitability remains positive but not especially strong",
            _ => "operating profitability is under pressure and limits cash conversion quality"
        };

        var balanceSheetSummary = cashBillions >= debtBillions
            ? "liquidity currently offsets debt obligations, supporting balance-sheet flexibility"
            : "debt obligations currently exceed cash balances, which limits balance-sheet flexibility";

        financialStatement = new FinancialStatement(
            identifier,
            $"financials:{identifier}",
            Math.Round(revenueGrowthPercent, 2, MidpointRounding.AwayFromZero),
            Math.Round(operatingMarginPercent, 2, MidpointRounding.AwayFromZero),
            Math.Round(freeCashFlowBillions, 2, MidpointRounding.AwayFromZero),
            Math.Round(cashBillions, 2, MidpointRounding.AwayFromZero),
            Math.Round(debtBillions, 2, MidpointRounding.AwayFromZero),
            efficiencySummary,
            balanceSheetSummary,
            $"{companyFacts.CompanyName} financial statements",
            fiscalYearEnd,
            TimestampType.AsOf,
            qualityScore,
            stanceScore);
        unavailableReason = null;
        return true;

        SecFactPoint? Missing(string figure)
        {
            missing.Add(figure);
            return null;
        }
    }

    // Total debt at the fiscal year end. A reported total can still leave out current maturities or commercial paper,
    // so it is compared with the non-current part plus the current part, and the larger figure is used.
    private static bool TryGetDebt(
        JsonElement facts,
        DateTimeOffset fiscalYearEnd,
        bool annualReportsOnly,
        [NotNullWhen(true)] out SecFactPoint? debt)
    {
        SecFactSelector.TryGetInstantFact(facts, TotalDebtConcepts, SecFactSelector.Usd, fiscalYearEnd, annualReportsOnly, out var total);

        SecFactPoint? parts = null;
        if (SecFactSelector.TryGetInstantFact(facts, NonCurrentDebtConcepts, SecFactSelector.Usd, fiscalYearEnd, annualReportsOnly, out var nonCurrent))
        {
            parts = SecFactSelector.TryGetInstantFact(facts, CurrentDebtConcepts, SecFactSelector.Usd, fiscalYearEnd, annualReportsOnly, out var current)
                ? nonCurrent with { Concept = $"{nonCurrent.Concept}+{current.Concept}", Value = nonCurrent.Value + current.Value }
                : nonCurrent;
        }

        debt = total is null || (parts is not null && parts.Value > total.Value) ? parts : total;
        return debt is not null;
    }

    private static string FormatDate(DateTimeOffset date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static decimal Clamp(decimal value, decimal minimum, decimal maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);

    private static IJobExecutionContextAccessor CreateHybridContextAccessor()
    {
        var accessor = new JobExecutionContextAccessor();
        accessor.Current = new JobExecutionContext
        {
            JobId = Guid.NewGuid(),
            Identifier = "TEST",
            CorrelationId = Guid.NewGuid().ToString("N"),
            ExecutionMode = ResearchExecutionMode.Hybrid,
            ResolvedTicker = new("TEST", "Test Company", "NASDAQ", "0000000000")
        };

        return accessor;
    }
}
