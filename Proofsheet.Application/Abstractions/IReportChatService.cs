using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IReportChatService
{
    Task<ReportChatExecutionResult> SendMessageAsync(
        CompletedResearchResult reportSnapshot,
        string userMessage,
        string previousResponseId,
        Func<ReportChatProviderEvent, Task>? onProviderEvent = null,
        CancellationToken cancellationToken = default);
}
