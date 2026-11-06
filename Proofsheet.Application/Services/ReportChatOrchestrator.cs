using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Services;

public sealed class ReportChatOrchestrator : IReportChatOrchestrator
{
    private const int MaxStreamHistoryEvents = 400;

    private readonly IReportChatAvailabilityService _reportChatAvailabilityService;
    private readonly IResearchJobQueryService _researchJobQueryService;
    private readonly IReportChatThreadStore _threadStore;
    private readonly IReportChatService _reportChatService;
    private readonly TimeProvider _timeProvider;

    public ReportChatOrchestrator(
        IReportChatAvailabilityService reportChatAvailabilityService,
        IResearchJobQueryService researchJobQueryService,
        IReportChatThreadStore threadStore,
        IReportChatService reportChatService,
        TimeProvider timeProvider)
    {
        _reportChatAvailabilityService = reportChatAvailabilityService;
        _researchJobQueryService = researchJobQueryService;
        _threadStore = threadStore;
        _reportChatService = reportChatService;
        _timeProvider = timeProvider;
    }

    public Task<ReportChatThreadCreatedResponse> CreateThreadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureChatConfigured();

        var record = _researchJobQueryService.Get(jobId);
        if (record is null)
        {
            throw new InvalidOperationException("The specified research job does not exist.");
        }

        if (record.Status != JobStatus.Completed || record.Result is null)
        {
            throw new InvalidOperationException("Report chat is available only for completed research jobs.");
        }

        var thread = _threadStore.Create(jobId, record.Result);
        return Task.FromResult(new ReportChatThreadCreatedResponse(thread.ThreadId, thread.JobId, thread.CreatedAtUtc));
    }

    public async Task SendMessageAsync(
        string threadId,
        string message,
        ReportChatStreamingCallbacks? streamingCallbacks = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("Thread id is required.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Message is required.");
        }

        EnsureChatConfigured();

        var thread = _threadStore.Get(threadId)
            ?? throw new InvalidOperationException("Report chat thread was not found or has expired.");

        await thread.SessionLock.WaitAsync(cancellationToken);
        try
        {
            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var streamToken = BeginStream(thread, streamCts);

            var startEvent = AppendStreamEvent(thread, "start", "conversation.start");
            await DispatchStreamEventAsync(streamingCallbacks, startEvent);

            try
            {
                var result = await _reportChatService.SendMessageAsync(
                    thread.ReportSnapshot,
                    message,
                    thread.PreviousResponseId,
                    onProviderEvent: providerEvent => HandleProviderEventAsync(thread, streamingCallbacks, providerEvent),
                    cancellationToken: streamToken);

                thread.PreviousResponseId = result.ResponseId;
                thread.LastUpdatedUtc = _timeProvider.GetUtcNow();
                thread.Transcript.Add(new ReportChatTranscriptTurn(message, result.Answer, result.CitedSourceIds));

                var doneEvent = AppendStreamEvent(thread, "done", "conversation.done", markStreamCompleted: true);
                await DispatchStreamEventAsync(streamingCallbacks, doneEvent);

                if (streamingCallbacks?.OnChatMessage is not null)
                {
                    await streamingCallbacks.OnChatMessage(
                        new ReportChatMessageResponse(
                            thread.ThreadId,
                            thread.JobId,
                            result.ResponseId,
                            "Responses",
                            result.Answer,
                            result.CitedSourceIds,
                            new ReportChatContextResponse(
                                thread.ReportSnapshot.Company,
                                thread.ReportSnapshot.Signal.ToString(),
                                thread.ReportSnapshot.Confidence)));
                }
            }
            catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
            {
                if (!HasRecentEventKind(thread, "canceled"))
                {
                    var canceled = AppendStreamEvent(
                        thread,
                        "canceled",
                        "conversation.canceled",
                        error: "Conversation canceled by caller.",
                        markStreamCompleted: true);
                    await DispatchStreamEventAsync(streamingCallbacks, canceled);
                }
                else
                {
                    CompleteStream(thread);
                }

                throw;
            }
            catch (Exception ex)
            {
                var errorEvent = AppendStreamEvent(
                    thread,
                    "error",
                    "conversation.error",
                    error: ex.Message,
                    markStreamCompleted: true);
                await DispatchStreamEventAsync(streamingCallbacks, errorEvent);
                throw;
            }
        }
        finally
        {
            thread.LastUpdatedUtc = _timeProvider.GetUtcNow();
            thread.SessionLock.Release();
        }
    }

    public Task<ReportChatStreamResumeResponse> ResumeStreamAsync(string threadId, long lastSequence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("Thread id is required.");
        }

        var thread = _threadStore.Get(threadId)
            ?? throw new InvalidOperationException("Report chat thread was not found or has expired.");

        if (lastSequence < 0)
        {
            lastSequence = 0;
        }

        lock (thread.StreamSync)
        {
            thread.LastUpdatedUtc = _timeProvider.GetUtcNow();
            return Task.FromResult(
                new ReportChatStreamResumeResponse(
                    thread.ThreadId,
                    thread.ActiveStreamId,
                    thread.StreamInProgress,
                    thread.ActiveStreamSequence,
                    thread.StreamEvents
                        .Where(x => x.Sequence > lastSequence)
                        .Select(CloneEvent)
                        .ToArray()));
        }
    }

    public Task<ReportChatStreamEventResponse> CancelStreamAsync(string threadId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException("Thread id is required.");
        }

        var thread = _threadStore.Get(threadId)
            ?? throw new InvalidOperationException("Report chat thread was not found or has expired.");

        CancellationTokenSource? activeCancellation;
        lock (thread.StreamSync)
        {
            if (!thread.StreamInProgress || thread.ActiveStreamCancellation is null)
            {
                throw new InvalidOperationException("No active report chat stream is running.");
            }

            activeCancellation = thread.ActiveStreamCancellation;
        }

        activeCancellation.Cancel();

        var canceled = AppendStreamEvent(
            thread,
            "canceled",
            "conversation.canceled",
            error: "Conversation canceled by caller.",
            markStreamCompleted: true);
        thread.LastUpdatedUtc = _timeProvider.GetUtcNow();
        return Task.FromResult(canceled);
    }

    private CancellationToken BeginStream(ReportChatThreadState thread, CancellationTokenSource cancellation)
    {
        lock (thread.StreamSync)
        {
            if (thread.ActiveStreamCancellation is not null)
            {
                try
                {
                    thread.ActiveStreamCancellation.Cancel();
                }
                catch
                {
                }

                thread.ActiveStreamCancellation.Dispose();
            }

            thread.ActiveStreamCancellation = cancellation;
            thread.ActiveStreamId = Guid.NewGuid().ToString("N");
            thread.ActiveStreamSequence = 0;
            thread.StreamInProgress = true;
            thread.StreamEvents.Clear();
            return cancellation.Token;
        }
    }

    private async Task HandleProviderEventAsync(
        ReportChatThreadState thread,
        ReportChatStreamingCallbacks? streamingCallbacks,
        ReportChatProviderEvent providerEvent)
    {
        if (string.Equals(providerEvent.Type, "response.output_text.delta", StringComparison.OrdinalIgnoreCase))
        {
            var deltaEvent = AppendStreamEvent(
                thread,
                "delta",
                providerEvent.Type,
                delta: providerEvent.TextDelta,
                payloadJson: providerEvent.PayloadJson);
            await DispatchStreamEventAsync(streamingCallbacks, deltaEvent);
            return;
        }

        if (IsToolEvent(providerEvent.Type))
        {
            var toolEvent = AppendStreamEvent(
                thread,
                "tool",
                providerEvent.Type,
                payloadJson: providerEvent.PayloadJson);
            await DispatchStreamEventAsync(streamingCallbacks, toolEvent);
        }
    }

    private ReportChatStreamEventResponse AppendStreamEvent(
        ReportChatThreadState thread,
        string kind,
        string providerEventType,
        string delta = "",
        string error = "",
        string payloadJson = "",
        bool markStreamCompleted = false)
    {
        lock (thread.StreamSync)
        {
            thread.ActiveStreamSequence++;
            var streamEvent = new ReportChatStreamEventResponse(
                thread.ThreadId,
                thread.JobId,
                thread.ActiveStreamId,
                thread.ActiveStreamSequence,
                kind,
                providerEventType,
                delta ?? string.Empty,
                error ?? string.Empty,
                _timeProvider.GetUtcNow());

            thread.StreamEvents.Add(streamEvent);
            if (thread.StreamEvents.Count > MaxStreamHistoryEvents)
            {
                thread.StreamEvents.RemoveRange(0, thread.StreamEvents.Count - MaxStreamHistoryEvents);
            }

            if (markStreamCompleted)
            {
                thread.StreamInProgress = false;
                if (thread.ActiveStreamCancellation is not null)
                {
                    thread.ActiveStreamCancellation.Dispose();
                    thread.ActiveStreamCancellation = null;
                }
            }

            return streamEvent with { };
        }
    }

    private async Task DispatchStreamEventAsync(ReportChatStreamingCallbacks? callbacks, ReportChatStreamEventResponse streamEvent)
    {
        if (callbacks?.OnStreamEvent is not null)
        {
            await callbacks.OnStreamEvent(streamEvent);
        }
    }

    private static bool HasRecentEventKind(ReportChatThreadState thread, string kind)
    {
        lock (thread.StreamSync)
        {
            return thread.StreamEvents.Count > 0
                && string.Equals(thread.StreamEvents[^1].Kind, kind, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool IsToolEvent(string providerEventType)
    {
        if (string.IsNullOrWhiteSpace(providerEventType))
        {
            return false;
        }

        return providerEventType.Contains("function_call", StringComparison.OrdinalIgnoreCase)
            || providerEventType.Contains("tool", StringComparison.OrdinalIgnoreCase);
    }

    private static ReportChatStreamEventResponse CloneEvent(ReportChatStreamEventResponse source) => source with { };

    private static void CompleteStream(ReportChatThreadState thread)
    {
        lock (thread.StreamSync)
        {
            thread.StreamInProgress = false;
            if (thread.ActiveStreamCancellation is not null)
            {
                thread.ActiveStreamCancellation.Dispose();
                thread.ActiveStreamCancellation = null;
            }
        }
    }

    private void EnsureChatConfigured()
    {
        if (!_reportChatAvailabilityService.IsConfigured)
        {
            throw new InvalidOperationException(_reportChatAvailabilityService.UnavailableReason);
        }
    }
}
