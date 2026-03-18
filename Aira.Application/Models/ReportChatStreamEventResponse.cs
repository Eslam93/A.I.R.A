namespace Aira.Application.Models;

public sealed record ReportChatStreamEventResponse(
    string ThreadId,
    Guid JobId,
    string StreamId,
    long Sequence,
    string Kind,
    string ProviderEventType,
    string Delta,
    string Error,
    DateTimeOffset CreatedUtc);
