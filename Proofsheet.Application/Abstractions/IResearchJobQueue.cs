using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IResearchJobQueue
{
    bool TryEnqueue(ResearchJobQueueItem item);

    ValueTask<ResearchJobQueueItem> DequeueAsync(CancellationToken cancellationToken);
}
