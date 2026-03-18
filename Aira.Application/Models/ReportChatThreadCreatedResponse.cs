namespace Aira.Application.Models;

public sealed record ReportChatThreadCreatedResponse(
    string ThreadId,
    Guid JobId,
    DateTimeOffset CreatedAtUtc);
