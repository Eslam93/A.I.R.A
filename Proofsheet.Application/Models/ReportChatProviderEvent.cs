namespace Proofsheet.Application.Models;

public sealed record ReportChatProviderEvent(
    string Type,
    string ResponseId,
    string TextDelta,
    string PayloadJson);
