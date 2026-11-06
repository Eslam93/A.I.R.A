namespace Proofsheet.Infrastructure.Options;

public sealed class AlphaVantageOptions
{
    public const string SectionName = "AlphaVantage";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://www.alphavantage.co/";

    public string? ApiKey { get; init; }
}
