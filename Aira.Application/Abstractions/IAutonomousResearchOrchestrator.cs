using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IAutonomousResearchOrchestrator
{
    Task<AutonomousResearchExecutionResult> ExecuteAsync(ResearchJobQueueItem item, CancellationToken cancellationToken);
}
