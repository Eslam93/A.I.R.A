using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IResearchJobProcessor
{
    Task ProcessAsync(ResearchJobQueueItem item, CancellationToken cancellationToken);
}
