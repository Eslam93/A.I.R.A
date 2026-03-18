using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IResearchJobQueue
{
    bool TryEnqueue(ResearchJobQueueItem item);

    ValueTask<ResearchJobQueueItem> DequeueAsync(CancellationToken cancellationToken);
}
