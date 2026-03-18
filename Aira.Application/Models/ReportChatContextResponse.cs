namespace Aira.Application.Models;

public sealed record ReportChatContextResponse(
    string Company,
    string Signal,
    int Confidence);
