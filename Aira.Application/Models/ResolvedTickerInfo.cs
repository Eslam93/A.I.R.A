namespace Aira.Application.Models;

public sealed record ResolvedTickerInfo(
    string Symbol,
    string CompanyName,
    string? Exchange,
    string CikPadded);
