namespace Proofsheet.Application.Models;

public sealed class ReportChatThreadState
{
    public required string ThreadId { get; init; }

    public required Guid JobId { get; init; }

    public required CompletedResearchResult ReportSnapshot { get; init; }

    public string PreviousResponseId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset LastUpdatedUtc { get; set; }

    public List<ReportChatTranscriptTurn> Transcript { get; } = new();

    public string ActiveStreamId { get; set; } = string.Empty;

    public long ActiveStreamSequence { get; set; }

    public bool StreamInProgress { get; set; }

    public List<ReportChatStreamEventResponse> StreamEvents { get; } = new();

    public CancellationTokenSource? ActiveStreamCancellation { get; set; }

    public object StreamSync { get; } = new();

    public SemaphoreSlim SessionLock { get; } = new(1, 1);
}
