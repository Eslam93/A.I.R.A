using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace Proofsheet.Infrastructure.Clients;

/// <summary>
/// Picks facts out of SEC EDGAR company facts (XBRL) so that every number in a statement describes the same fiscal year.
/// </summary>
/// <remarks>
/// <para>
/// In company facts, a fact's <c>fp</c> and <c>form</c> fields describe the filing that reported it, not the period the
/// fact covers. A 10-K also carries fourth-quarter figures, and 10-Qs can carry trailing-twelve-month figures. A fiscal
/// year is therefore a fact that spans 350 to 380 days (which covers 52/53-week years) and comes from an annual report.
/// </para>
/// <para>
/// Companies also move between concepts over time, for example from <c>Revenues</c> to
/// <c>RevenueFromContractWithCustomerExcludingAssessedTax</c>, and stop reporting the old one. The concept that is still
/// being reported wins; list order only breaks ties.
/// </para>
/// </remarks>
public static class SecFactSelector
{
    public const string Usd = "USD";

    private const string Taxonomy = "us-gaap";
    private const int MinFiscalYearDays = 350;
    private const int MaxFiscalYearDays = 380;
    private static readonly TimeSpan DateTolerance = TimeSpan.FromDays(7);
    private static readonly string[] AnnualForms = ["10-K", "10-K/A", "10-KT", "10-KT/A", "20-F", "20-F/A", "40-F", "40-F/A"];

    /// <summary>
    /// Gets the latest fiscal year and the fiscal year right before it. The concept whose latest fiscal year is newest
    /// is used for both; earlier concepts in <paramref name="concepts"/> win ties.
    /// </summary>
    public static bool TryGetLatestFiscalYearPair(
        JsonElement companyFacts,
        IReadOnlyList<string> concepts,
        string unit,
        [NotNullWhen(true)] out SecFactPoint? latest,
        [NotNullWhen(true)] out SecFactPoint? previous)
    {
        latest = null;
        previous = null;

        IReadOnlyList<SecFactPoint>? freshest = null;
        foreach (var concept in concepts)
        {
            var fiscalYears = GetFiscalYears(companyFacts, concept, unit);
            if (fiscalYears.Count > 0 && (freshest is null || fiscalYears[0].EndDate > freshest[0].EndDate))
            {
                freshest = fiscalYears;
            }
        }

        if (freshest is null)
        {
            return false;
        }

        var newest = LatestFiledNear(freshest, freshest[0].EndDate)!;
        var prior = LatestFiledNear(freshest, newest.StartDate!.Value.AddDays(-1));
        if (prior is null)
        {
            return false;
        }

        latest = newest;
        previous = prior;
        return true;
    }

    /// <summary>
    /// Gets the end of the newest fiscal year that any of the concepts reports more than a week after
    /// <paramref name="fiscalYearEnd"/>.
    /// </summary>
    public static bool TryGetFiscalYearAfter(
        JsonElement companyFacts,
        IReadOnlyList<string> concepts,
        string unit,
        DateTimeOffset fiscalYearEnd,
        out DateTimeOffset newerFiscalYearEnd)
    {
        newerFiscalYearEnd = default;
        foreach (var concept in concepts)
        {
            var fiscalYears = GetFiscalYears(companyFacts, concept, unit);
            if (fiscalYears.Count > 0 &&
                fiscalYears[0].EndDate - fiscalYearEnd > DateTolerance &&
                fiscalYears[0].EndDate > newerFiscalYearEnd)
            {
                newerFiscalYearEnd = fiscalYears[0].EndDate;
            }
        }

        return newerFiscalYearEnd != default;
    }

    /// <summary>
    /// Gets the fiscal year that ends on <paramref name="fiscalYearEnd"/> from the first concept that reports it.
    /// </summary>
    public static bool TryGetFiscalYearFact(
        JsonElement companyFacts,
        IReadOnlyList<string> concepts,
        string unit,
        DateTimeOffset fiscalYearEnd,
        [NotNullWhen(true)] out SecFactPoint? fact)
    {
        foreach (var concept in concepts)
        {
            fact = LatestFiledNear(GetFiscalYears(companyFacts, concept, unit), fiscalYearEnd);
            if (fact is not null)
            {
                return true;
            }
        }

        fact = null;
        return false;
    }

    /// <summary>
    /// Gets the balance on <paramref name="date"/> from the first concept that reports it. With
    /// <paramref name="annualReportsOnly"/>, only annual reports count, so a later 10-Q that re-tags the year-end
    /// comparative cannot change a fiscal year's balance.
    /// </summary>
    public static bool TryGetInstantFact(
        JsonElement companyFacts,
        IReadOnlyList<string> concepts,
        string unit,
        DateTimeOffset date,
        bool annualReportsOnly,
        [NotNullWhen(true)] out SecFactPoint? fact)
    {
        foreach (var concept in concepts)
        {
            // A balance a day after the year end can be a different (opening) balance, so the closest date wins first.
            fact = GetInstants(companyFacts, concept, unit, annualReportsOnly)
                .Where(item => IsNear(item.EndDate, date))
                .OrderBy(item => (item.EndDate - date).Duration())
                .ThenByDescending(item => item.FiledDate)
                .FirstOrDefault();
            if (fact is not null)
            {
                return true;
            }
        }

        fact = null;
        return false;
    }

    private static IReadOnlyList<SecFactPoint> GetFiscalYears(JsonElement companyFacts, string concept, string unit) =>
        ReadFacts(companyFacts, concept, unit)
            .Where(fact => fact.StartDate is not null &&
                IsAnnualReport(fact) &&
                (fact.EndDate - fact.StartDate.Value).TotalDays is >= MinFiscalYearDays and <= MaxFiscalYearDays)
            .GroupBy(fact => (fact.StartDate, fact.EndDate))
            .Select(LatestFiled)
            .OrderByDescending(fact => fact.EndDate)
            .ToArray();

    private static IReadOnlyList<SecFactPoint> GetInstants(JsonElement companyFacts, string concept, string unit, bool annualReportsOnly) =>
        ReadFacts(companyFacts, concept, unit)
            .Where(fact => fact.StartDate is null && (!annualReportsOnly || IsAnnualReport(fact)))
            .GroupBy(fact => fact.EndDate)
            .Select(LatestFiled)
            .OrderByDescending(fact => fact.EndDate)
            .ToArray();

    private static bool IsAnnualReport(SecFactPoint fact) =>
        fact.Form is not null && AnnualForms.Contains(fact.Form, StringComparer.OrdinalIgnoreCase);

    // The same period is reported again in later filings, sometimes restated. The latest filing wins.
    private static SecFactPoint LatestFiled(IEnumerable<SecFactPoint> facts) =>
        facts.MaxBy(fact => fact.FiledDate)!;

    // Later filings sometimes re-tag a fiscal year with dates shifted by a day or two (and a restated value), so every
    // fiscal year ending within a week of the target counts, and the latest filing wins.
    private static SecFactPoint? LatestFiledNear(IEnumerable<SecFactPoint> fiscalYears, DateTimeOffset end) =>
        fiscalYears
            .Where(fact => IsNear(fact.EndDate, end))
            .OrderByDescending(fact => fact.FiledDate)
            .ThenBy(fact => (fact.EndDate - end).Duration())
            .FirstOrDefault();

    private static bool IsNear(DateTimeOffset value, DateTimeOffset target) =>
        (value - target).Duration() <= DateTolerance;

    private static IEnumerable<SecFactPoint> ReadFacts(JsonElement companyFacts, string concept, string unit)
    {
        if (!companyFacts.TryGetProperty("facts", out var facts) ||
            !facts.TryGetProperty(Taxonomy, out var taxonomy) ||
            !taxonomy.TryGetProperty(concept, out var conceptElement) ||
            !conceptElement.TryGetProperty("units", out var units) ||
            !units.TryGetProperty(unit, out var points) ||
            points.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var point in points.EnumerateArray())
        {
            if (!TryGetDecimal(point, "val", out var value) ||
                !TryGetDate(point, "end", out var endDate) ||
                !TryGetDate(point, "filed", out var filedDate))
            {
                continue;
            }

            DateTimeOffset? startDate = TryGetDate(point, "start", out var start) ? start : null;
            yield return new SecFactPoint(concept, value, startDate, endDate, filedDate, TryGetString(point, "form"), TryGetString(point, "accn"));
        }
    }

    private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryGetDate(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParseExact(property.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

/// <summary>A single XBRL fact. <see cref="StartDate"/> is null for balances (instants).</summary>
public sealed record SecFactPoint(
    string Concept,
    decimal Value,
    DateTimeOffset? StartDate,
    DateTimeOffset EndDate,
    DateTimeOffset FiledDate,
    string? Form,
    string? AccessionNumber);
