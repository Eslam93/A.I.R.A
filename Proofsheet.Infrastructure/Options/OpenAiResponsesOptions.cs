namespace Proofsheet.Infrastructure.Options;

public sealed class OpenAiResponsesOptions
{
    public const string SectionName = "OpenAiResponses";

    public bool Enabled { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.openai.com";

    public string Model { get; init; } = "gpt-5.4-nano";

    public string ReportModel { get; init; } = string.Empty;

    public string ChatModel { get; init; } = string.Empty;

    public string CoordinatorModel { get; init; } = string.Empty;

    public string SpecialistModel { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 120;

    public bool EnableStreaming { get; init; } = true;

    public int MaxToolRounds { get; init; } = 5;

    public string PromptFilePath { get; init; } = "Prompts/report-chat-system-prompt.txt";

    public string ReportPromptFilePath { get; init; } = "Prompts/report-synthesis-system-prompt.txt";

    public string ChatPromptFilePath { get; init; } = "Prompts/report-chat-system-prompt.txt";

    public string CoordinatorPromptFilePath { get; init; } = "Prompts/autonomous-coordinator-system-prompt.txt";

    public string SpecialistPromptFilePath { get; init; } = "Prompts/autonomous-specialist-system-prompt.txt";

    public int ReportValidationRetries { get; init; } = 1;

    public int CoordinatorMaxToolRounds { get; init; } = 8;

    public int SpecialistCritiqueRetries { get; init; } = 1;

    public bool AllowAutonomousStandardFallback { get; init; }
}
