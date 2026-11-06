using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aira.Infrastructure.Clients;

namespace Aira.Tests.Unit.Providers;

public sealed class SecFactSelectorTests
{
    private const string Revenues = "Revenues";
    private const string ContractRevenue = "RevenueFromContractWithCustomerExcludingAssessedTax";

    [Fact]
    public void Fiscal_Year_Pair_Ignores_A_Fourth_Quarter_Reported_In_The_Annual_Report()
    {
        // A 10-K reports the year and its fourth quarter with the same end date, and both are tagged fp=FY.
        using var facts = Facts(
            Fact(Revenues, "2024-09-29", "2025-09-27", 400, "10-K", "2025-10-31"),
            Fact(Revenues, "2025-06-29", "2025-09-27", 100, "10-K", "2025-10-31"),
            Fact(Revenues, "2023-10-01", "2024-09-28", 380, "10-K", "2024-11-01"));

        Assert.True(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues], "USD", out var latest, out var previous));
        Assert.Equal(400m, latest.Value);
        Assert.Equal(380m, previous.Value);
    }

    [Fact]
    public void Fiscal_Year_Pair_Ignores_Trailing_Twelve_Months_From_Quarterly_Reports()
    {
        using var facts = Facts(
            Fact(Revenues, "2024-09-29", "2025-09-27", 400, "10-K", "2025-10-31"),
            Fact(Revenues, "2023-10-01", "2024-09-28", 380, "10-K", "2024-11-01"),
            Fact(Revenues, "2024-12-29", "2025-12-27", 410, "10-Q", "2026-01-30", fiscalPeriod: "Q1"));

        Assert.True(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues], "USD", out var latest, out _));
        Assert.Equal(Date("2025-09-27"), latest.EndDate);
        Assert.Equal(400m, latest.Value);
    }

    [Fact]
    public void Fiscal_Year_Pair_Prefers_The_Concept_That_Is_Still_Reported()
    {
        using var facts = Facts(
            Fact(Revenues, "2017-10-01", "2018-09-29", 265, "10-K", "2018-11-05"),
            Fact(Revenues, "2016-09-25", "2017-09-30", 229, "10-K", "2018-11-05"),
            Fact(ContractRevenue, "2024-09-29", "2025-09-27", 416, "10-K", "2025-10-31"),
            Fact(ContractRevenue, "2023-10-01", "2024-09-28", 391, "10-K", "2025-10-31"));

        Assert.True(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues, ContractRevenue], "USD", out var latest, out var previous));
        Assert.Equal(ContractRevenue, latest.Concept);
        Assert.Equal(416m, latest.Value);
        Assert.Equal(391m, previous.Value);
    }

    [Fact]
    public void Fiscal_Year_Pair_Breaks_Ties_By_List_Order()
    {
        using var facts = Facts(
            Fact(Revenues, "2025-01-01", "2025-12-31", 95, "10-K", "2026-01-29"),
            Fact(Revenues, "2024-01-01", "2024-12-31", 98, "10-K", "2026-01-29"),
            Fact(ContractRevenue, "2025-01-01", "2025-12-31", 94, "10-K", "2026-01-29"),
            Fact(ContractRevenue, "2024-01-01", "2024-12-31", 97, "10-K", "2026-01-29"));

        Assert.True(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues, ContractRevenue], "USD", out var latest, out _));
        Assert.Equal(Revenues, latest.Concept);
        Assert.Equal(95m, latest.Value);
    }

    [Fact]
    public void Fiscal_Year_Pair_Accepts_A_53_Week_Year()
    {
        using var facts = Facts(
            Fact(Revenues, "2022-09-25", "2023-09-30", 371, "10-K", "2023-11-03"),
            Fact(Revenues, "2021-09-26", "2022-09-24", 364, "10-K", "2023-11-03"));

        Assert.True(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues], "USD", out var latest, out var previous));
        Assert.Equal(371m, latest.Value);
        Assert.Equal(364m, previous.Value);
    }

    [Fact]
    public void Fiscal_Year_Pair_Uses_The_Latest_Filing_For_A_Restated_Year()
    {
        using var facts = Facts(
            Fact(Revenues, "2024-01-01", "2024-12-31", 380, "10-K", "2025-02-14"),
            Fact(Revenues, "2025-01-01", "2025-12-31", 400, "10-K", "2026-02-13"),
            Fact(Revenues, "2024-01-01", "2024-12-31", 375, "10-K", "2026-02-13"));

        Assert.True(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues], "USD", out _, out var previous));
        Assert.Equal(375m, previous.Value);
    }

    [Fact]
    public void Fiscal_Year_Pair_Requires_Consecutive_Years()
    {
        using var facts = Facts(
            Fact(Revenues, "2025-01-01", "2025-12-31", 400, "10-K", "2026-02-13"),
            Fact(Revenues, "2023-01-01", "2023-12-31", 360, "10-K", "2024-02-14"));

        Assert.False(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues], "USD", out _, out _));
    }

    [Fact]
    public void Facts_In_Other_Currencies_Are_Ignored()
    {
        using var facts = Facts(
            Fact(Revenues, "2025-01-01", "2025-12-31", 400, "10-K", "2026-02-13", unit: "EUR"),
            Fact(Revenues, "2024-01-01", "2024-12-31", 380, "10-K", "2025-02-14", unit: "EUR"));

        Assert.False(SecFactSelector.TryGetLatestFiscalYearPair(facts.RootElement, [Revenues], "USD", out _, out _));
    }

    [Fact]
    public void Fiscal_Year_Fact_Must_Match_The_Requested_Year()
    {
        using var facts = Facts(
            Fact("OperatingIncomeLoss", "2024-09-29", "2025-09-27", 120, "10-K", "2025-10-31"),
            Fact("OperatingIncomeLoss", "2023-10-01", "2024-09-28", 110, "10-K", "2024-11-01"));

        Assert.True(SecFactSelector.TryGetFiscalYearFact(facts.RootElement, ["OperatingIncomeLoss"], "USD", Date("2024-09-28"), out var fact));
        Assert.Equal(110m, fact.Value);
        Assert.False(SecFactSelector.TryGetFiscalYearFact(facts.RootElement, ["OperatingIncomeLoss"], "USD", Date("2026-09-26"), out _));
    }

    [Fact]
    public void Instant_Fact_Uses_The_Balance_On_The_Requested_Date_From_The_First_Concept_That_Has_It()
    {
        // The first concept stopped being reported in 2018; the second has both the year-end and a later quarter-end balance.
        using var facts = Facts(
            Fact("LongTermDebtAndCapitalLeaseObligations", null, "2018-12-31", 9, "10-K", "2019-02-19"),
            Fact("LongTermDebt", null, "2025-12-31", 7, "10-K", "2026-01-29"),
            Fact("LongTermDebt", null, "2026-09-30", 8, "10-Q", "2026-10-23", fiscalPeriod: "Q3"));

        Assert.True(SecFactSelector.TryGetInstantFact(
            facts.RootElement,
            ["LongTermDebtAndCapitalLeaseObligations", "LongTermDebt"],
            "USD",
            Date("2025-12-31"),
            annualReportsOnly: false,
            out var fact));
        Assert.Equal("LongTermDebt", fact.Concept);
        Assert.Equal(7m, fact.Value);
    }

    [Fact]
    public void Fiscal_Year_Re_Tagged_With_Shifted_Dates_Uses_The_Latest_Filing()
    {
        // The original 10-K tagged FY2016 as 2015-11-01..2016-10-31. Later 10-Ks re-tagged it as 2015-11-02..2016-10-30
        // with a restated value.
        using var facts = Facts(
            Fact("OperatingIncomeLoss", "2015-11-01", "2016-10-31", 2.589m, "10-K", "2016-12-19"),
            Fact("OperatingIncomeLoss", "2015-11-02", "2016-10-30", 2.609m, "10-K", "2017-12-18"));

        Assert.True(SecFactSelector.TryGetFiscalYearFact(facts.RootElement, ["OperatingIncomeLoss"], "USD", Date("2016-10-31"), out var fact));
        Assert.Equal(2.609m, fact.Value);
    }

    [Fact]
    public void Annual_Report_Balances_Ignore_A_Later_Quarterly_Re_Tag()
    {
        // The 10-K reports year-end debt under LongTermDebt. A later 10-Q re-tags the same year-end column under a concept
        // that comes first in the list.
        using var facts = Facts(
            Fact("LongTermDebt", null, "2025-12-27", 46.35m, "10-K", "2026-02-06"),
            Fact("LongTermDebtAndCapitalLeaseObligations", null, "2025-12-27", 42.32m, "10-Q", "2026-04-17", fiscalPeriod: "Q1"));
        string[] concepts = ["LongTermDebtAndCapitalLeaseObligations", "LongTermDebt"];

        Assert.True(SecFactSelector.TryGetInstantFact(facts.RootElement, concepts, "USD", Date("2025-12-27"), annualReportsOnly: true, out var annual));
        Assert.Equal(46.35m, annual.Value);
        Assert.True(SecFactSelector.TryGetInstantFact(facts.RootElement, concepts, "USD", Date("2025-12-27"), annualReportsOnly: false, out var any));
        Assert.Equal(42.32m, any.Value);
    }

    [Fact]
    public void Instant_Fact_Prefers_The_Exact_Date_Over_A_Later_Balance_Nearby()
    {
        // A balance a day after the year end can be an opening balance after an accounting change.
        using var facts = Facts(
            Fact("LongTermDebt", null, "2019-11-03", 30m, "10-K", "2019-12-17"),
            Fact("LongTermDebt", null, "2019-11-04", 31m, "10-Q", "2020-02-27", fiscalPeriod: "Q1"));

        Assert.True(SecFactSelector.TryGetInstantFact(facts.RootElement, ["LongTermDebt"], "USD", Date("2019-11-03"), annualReportsOnly: false, out var fact));
        Assert.Equal(30m, fact.Value);
    }

    [Fact]
    public void Fiscal_Year_After_Finds_A_Newer_Year_Under_Another_Concept()
    {
        using var facts = Facts(
            Fact("OperatingIncomeLoss", "2024-09-29", "2025-09-27", 120, "10-K", "2025-10-31"),
            Fact("OperatingIncomeLoss", "2025-09-28", "2026-09-26", 130, "10-K", "2026-10-30"));

        Assert.True(SecFactSelector.TryGetFiscalYearAfter(facts.RootElement, ["OperatingIncomeLoss"], "USD", Date("2025-09-27"), out var newer));
        Assert.Equal(Date("2026-09-26"), newer);
        Assert.False(SecFactSelector.TryGetFiscalYearAfter(facts.RootElement, ["OperatingIncomeLoss"], "USD", Date("2026-09-26"), out _));
    }

    private static DateTimeOffset Date(string value) =>
        DateTimeOffset.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static TestFact Fact(
        string concept,
        string? start,
        string end,
        decimal value,
        string form,
        string filed,
        string unit = "USD",
        string fiscalPeriod = "FY") =>
        new(concept, start, end, value, form, filed, unit, fiscalPeriod);

    private static JsonDocument Facts(params TestFact[] facts)
    {
        var usGaap = new JsonObject();
        foreach (var concept in facts.GroupBy(fact => fact.Concept))
        {
            var units = new JsonObject();
            foreach (var unit in concept.GroupBy(fact => fact.Unit))
            {
                var points = new JsonArray();
                foreach (var fact in unit)
                {
                    var point = new JsonObject
                    {
                        ["val"] = fact.Value,
                        ["end"] = fact.End,
                        ["fp"] = fact.FiscalPeriod,
                        ["form"] = fact.Form,
                        ["filed"] = fact.Filed
                    };
                    if (fact.Start is not null)
                    {
                        point["start"] = fact.Start;
                    }

                    points.Add(point);
                }

                units[unit.Key] = points;
            }

            usGaap[concept.Key] = new JsonObject { ["units"] = units };
        }

        return JsonDocument.Parse(new JsonObject { ["facts"] = new JsonObject { ["us-gaap"] = usGaap } }.ToJsonString());
    }

    private sealed record TestFact(
        string Concept,
        string? Start,
        string End,
        decimal Value,
        string Form,
        string Filed,
        string Unit,
        string FiscalPeriod);
}
