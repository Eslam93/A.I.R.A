using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IReportChatOrchestrator
{
    Task<ReportChatThreadCreatedResponse> CreateThreadAsync(Guid jobId, CancellationToken cancellationToken);

    Task SendMessageAsync(
        string threadId,
        string message,
        ReportChatStreamingCallbacks? streamingCallbacks = null,
        CancellationToken cancellationToken = default);

    Task<ReportChatStreamResumeResponse> ResumeStreamAsync(string threadId, long lastSequence, CancellationToken cancellationToken);

    Task<ReportChatStreamEventResponse> CancelStreamAsync(string threadId, CancellationToken cancellationToken);
}
