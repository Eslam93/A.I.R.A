using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IReportChatService
{
    Task<ReportChatExecutionResult> SendMessageAsync(
        CompletedResearchResult reportSnapshot,
        string userMessage,
        string previousResponseId,
        Func<ReportChatProviderEvent, Task>? onProviderEvent = null,
        CancellationToken cancellationToken = default);
}
