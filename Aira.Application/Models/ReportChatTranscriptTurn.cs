namespace Aira.Application.Models;

public sealed record ReportChatTranscriptTurn(
    string UserMessage,
    string AssistantAnswer,
    IReadOnlyList<string> CitedSourceIds);
