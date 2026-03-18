using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IReportChatThreadStore
{
    ReportChatThreadState Create(Guid jobId, CompletedResearchResult reportSnapshot);

    ReportChatThreadState? Get(string threadId);

    bool Remove(string threadId);
}
