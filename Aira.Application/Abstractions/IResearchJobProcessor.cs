using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IResearchJobProcessor
{
    Task ProcessAsync(ResearchJobQueueItem item, CancellationToken cancellationToken);
}
