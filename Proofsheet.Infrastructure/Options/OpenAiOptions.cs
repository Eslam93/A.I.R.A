namespace Proofsheet.Infrastructure.Options;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    public bool Enabled { get; init; }

    public string? ApiKey { get; init; }

    public string? Model { get; init; }

    public string? BaseUrl { get; init; }

    public int TimeoutSeconds { get; init; } = 30;
}
