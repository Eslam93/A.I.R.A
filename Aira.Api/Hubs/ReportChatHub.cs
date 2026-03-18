using Aira.Application.Abstractions;
using Aira.Application.Models;
using Microsoft.AspNetCore.SignalR;

namespace Aira.Api.Hubs;

public sealed class ReportChatHub : Hub
{
    private readonly IReportChatOrchestrator _reportChatOrchestrator;

    public ReportChatHub(IReportChatOrchestrator reportChatOrchestrator)
    {
        _reportChatOrchestrator = reportChatOrchestrator;
    }

    public async Task<ReportChatThreadCreatedResponse> CreateThread(Guid jobId)
    {
        try
        {
            return await _reportChatOrchestrator.CreateThreadAsync(jobId, Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task SendMessage(string threadId, string message)
    {
        try
        {
            var callbacks = new ReportChatStreamingCallbacks
            {
                OnStreamEvent = streamEvent => Clients.Caller.SendAsync("ReceiveStreamEvent", streamEvent, Context.ConnectionAborted),
                OnChatMessage = chatMessage => Clients.Caller.SendAsync("ReceiveChatMessage", chatMessage, Context.ConnectionAborted)
            };

            await _reportChatOrchestrator.SendMessageAsync(threadId, message, callbacks, Context.ConnectionAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task<ReportChatStreamResumeResponse> ResumeStream(string threadId, long lastSequence)
    {
        try
        {
            return await _reportChatOrchestrator.ResumeStreamAsync(threadId, lastSequence, Context.ConnectionAborted);
        }
        catch (Exception ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public async Task<bool> CancelStream(string threadId)
    {
        try
        {
            var canceledEvent = await _reportChatOrchestrator.CancelStreamAsync(threadId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("ReceiveStreamEvent", canceledEvent, Context.ConnectionAborted);
            return true;
        }
        catch (Exception ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
