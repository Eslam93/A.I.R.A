using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IAutonomousResearchOrchestrator
{
    Task<AutonomousResearchExecutionResult> ExecuteAsync(ResearchJobQueueItem item, CancellationToken cancellationToken);
}
