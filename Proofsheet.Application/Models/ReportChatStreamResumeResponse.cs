namespace Proofsheet.Application.Models;

public sealed record ReportChatStreamResumeResponse(
    string ThreadId,
    string StreamId,
    bool InProgress,
    long LatestSequence,
    IReadOnlyList<ReportChatStreamEventResponse> Events);
