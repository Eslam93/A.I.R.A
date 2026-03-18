using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IAutonomousJobSessionStore
{
    AutonomousJobSession Create(ResearchJobQueueItem queueItem);

    AutonomousJobSession? Get(Guid jobId);

    void Remove(Guid jobId);
}
