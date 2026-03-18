namespace Aira.Application.Models;

public sealed class ReportChatStreamingCallbacks
{
    public Func<ReportChatStreamEventResponse, Task>? OnStreamEvent { get; init; }

    public Func<ReportChatMessageResponse, Task>? OnChatMessage { get; init; }
}
