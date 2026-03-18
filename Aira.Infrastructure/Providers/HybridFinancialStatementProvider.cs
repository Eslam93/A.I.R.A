using Aira.Domain.Contracts;
using Aira.Domain.Enums;
using Aira.Domain.Models;
using Aira.Infrastructure.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aira.Infrastructure.Providers;

public sealed class HybridFinancialStatementProvider : IFinancialStatementProvider
{
    private static readonly string[] RevenueConcepts =
    [
        "Revenues",
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "SalesRevenueNet"
    ];

    private static readonly string[] OperatingIncomeConcepts =
    [
        "OperatingIncomeLoss"
    ];

    private static readonly string[] CashFromOperationsConcepts =
    [
        "NetCashProvidedByUsedInOperatingActivities",
        "NetCashProvidedByUsedInOperatingActivitiesContinuingOperations"
    ];

    private static readonly string[] CapitalExpenditureConcepts =
    [
        "PaymentsToAcquirePropertyPlantAndEquipment",
        "PropertyPlantAndEquipmentAdditions",
        "CapitalExpendituresIncurredButNotYetPaid"
    ];

    private static readonly string[] CashConcepts =
    [
        "CashAndCashEquivalentsAtCarryingValue",
        "CashCashEquivalentsRestrictedCashAndRestrictedCashEquivalents"
    ];

    private static readonly string[] TotalDebtConcepts =
    [
        "LongTermDebtAndCapitalLeaseObligations",
        "LongTermDebtAndFinanceLeaseObligations",
        "LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities",
        "LongTermDebt"
    ];

    private static readonly string[] CurrentDebtConcepts =
    [
        "LongTermDebtCurrent",
        "ShortTermBorrowings",
        "LongTermDebtAndCapitalLeaseObligationsCurrent"
    ];

    private static readonly string[] NonCurrentDebtConcepts =
    [
        "LongTermDebtNoncurrent",
        "LongTermDebtAndCapitalLeaseObligationsNoncurrent",
        "LongTermDebtAndFinanceLeaseObligationsNoncurrent"
    ];

    private readonly MockFinancialStatementProvider _mockProvider;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;
    private readonly SecEdgarClient _secEdgarClient;
    private readonly ILogger<HybridFinancialStatementProvider> _logger;

    [ActivatorUtilitiesConstructor]
    public HybridFinancialStatementProvider(
        MockFinancialStatementProvider mockProvider,
        IJobExecutionContextAccessor jobExecutionContextAccessor,
        SecEdgarClient secEdgarClient,
        ILogger<HybridFinancialStatementProvider> logger)
    {
        _mockProvider = mockProvider;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
    }

    public HybridFinancialStatementProvider(
        MockFinancialStatementProvider mockProvider,
        SecEdgarClient secEdgarClient,
        ILogger<HybridFinancialStatementProvider> logger)
        : this(mockProvider, CreateHybridContextAccessor(), secEdgarClient, logger)
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
            if (companyFacts is null ||
                !TryBuildLiveStatement(identifier, companyFacts, out var statement))
            {
                return mode == ResearchExecutionMode.LiveRequired
                    ? ProviderResult<FinancialStatement>.Empty(errorMessage: "SEC EDGAR did not return enough financial facts.", origin: ProviderOrigin.Live, providerName: "SEC EDGAR")
                    : ToMockFallback(fallback, "SEC EDGAR did not return enough financial facts, so the seeded mock statements were used.");
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
        out FinancialStatement financialStatement)
    {
        financialStatement = null!;

        if (!SecEdgarClient.TryGetLatestTwoAnnualFacts(companyFacts.CompanyFactsRoot, RevenueConcepts, ["USD"], out var latestRevenue, out var previousRevenue) ||
            !SecEdgarClient.TryGetLatestAnnualFact(companyFacts.CompanyFactsRoot, OperatingIncomeConcepts, ["USD"], out var latestOperatingIncome) ||
            !SecEdgarClient.TryGetLatestAnnualFact(companyFacts.CompanyFactsRoot, CashFromOperationsConcepts, ["USD"], out var latestOperatingCashFlow) ||
            !SecEdgarClient.TryGetLatestAnnualFact(companyFacts.CompanyFactsRoot, CapitalExpenditureConcepts, ["USD"], out var latestCapitalExpenditure) ||
            !SecEdgarClient.TryGetLatestInstantFact(companyFacts.CompanyFactsRoot, CashConcepts, ["USD"], out var latestCash))
        {
            return false;
        }

        var hasDebt = SecEdgarClient.TryGetLatestInstantFact(companyFacts.CompanyFactsRoot, TotalDebtConcepts, ["USD"], out var latestDebt);
        if (!hasDebt &&
            SecEdgarClient.TryGetLatestInstantFact(companyFacts.CompanyFactsRoot, CurrentDebtConcepts, ["USD"], out var currentDebt) &&
            SecEdgarClient.TryGetLatestInstantFact(companyFacts.CompanyFactsRoot, NonCurrentDebtConcepts, ["USD"], out var nonCurrentDebt))
        {
            latestDebt = currentDebt with { Value = currentDebt.Value + nonCurrentDebt.Value };
            hasDebt = true;
        }

        if (!hasDebt || latestRevenue.Value == 0m)
        {
            return false;
        }

        var revenueGrowthPercent = previousRevenue.Value == 0m
            ? 0m
            : ((latestRevenue.Value - previousRevenue.Value) / Math.Abs(previousRevenue.Value)) * 100m;
        var operatingMarginPercent = (latestOperatingIncome.Value / latestRevenue.Value) * 100m;
        var freeCashFlowBillions = (latestOperatingCashFlow.Value - latestCapitalExpenditure.Value) / 1_000_000_000m;
        var cashBillions = latestCash.Value / 1_000_000_000m;
        var debtBillions = latestDebt.Value / 1_000_000_000m;

        var growthSignal = Clamp(revenueGrowthPercent / 20m, -1m, 1m);
        var marginSignal = Clamp((operatingMarginPercent - 10m) / 20m, -1m, 1m);
        var balanceSignal = Clamp((cashBillions - debtBillions) / Math.Max(Math.Abs(cashBillions) + Math.Abs(debtBillions), 1m), -1m, 1m);
        var stanceScore = Math.Round(Clamp((growthSignal * 0.40m) + (marginSignal * 0.40m) + (balanceSignal * 0.20m), -1m, 1m), 2, MidpointRounding.AwayFromZero);

        var qualityScore = Math.Round(
            Clamp(
                0.70m +
                (latestRevenue.EndDate > DateTimeOffset.UtcNow.AddMonths(-18) ? 0.10m : 0m) +
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
            latestRevenue.EndDate,
            TimestampType.AsOf,
            qualityScore,
            stanceScore);
        return true;
    }

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
