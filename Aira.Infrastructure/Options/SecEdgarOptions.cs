namespace Aira.Infrastructure.Options;

public sealed class SecEdgarOptions
{
    public const string SectionName = "SecEdgar";

    public bool Enabled { get; init; }

    public string DataBaseUrl { get; init; } = "https://data.sec.gov/";

    public string MappingUrl { get; init; } = "https://www.sec.gov/files/company_tickers_exchange.json";

    public string? UserAgent { get; init; } = "AIRA/1.0 local@example.com";
}
