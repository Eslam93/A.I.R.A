namespace Proofsheet.Application.Models;

public sealed record TickerSearchItemResponse(
    string Symbol,
    string CompanyName,
    string? Exchange,
    string Cik);
