namespace Aira.Application.Models;

public sealed record ReportChatExecutionResult(
    string ResponseId,
    string Answer,
    IReadOnlyList<string> CitedSourceIds);
