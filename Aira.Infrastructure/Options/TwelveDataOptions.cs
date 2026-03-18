namespace Aira.Infrastructure.Options;

public sealed class TwelveDataOptions
{
    public const string SectionName = "TwelveData";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://api.twelvedata.com/";

    public string? ApiKey { get; init; }
}
