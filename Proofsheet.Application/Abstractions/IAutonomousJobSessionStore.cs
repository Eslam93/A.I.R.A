using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IAutonomousJobSessionStore
{
    AutonomousJobSession Create(ResearchJobQueueItem queueItem);

    AutonomousJobSession? Get(Guid jobId);

    void Remove(Guid jobId);
}
