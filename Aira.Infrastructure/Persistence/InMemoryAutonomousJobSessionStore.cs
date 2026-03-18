using System.Collections.Concurrent;
using Aira.Application.Abstractions;
using Aira.Application.Models;

namespace Aira.Infrastructure.Persistence;

public sealed class InMemoryAutonomousJobSessionStore : IAutonomousJobSessionStore
{
    private readonly ConcurrentDictionary<Guid, AutonomousJobSession> _sessions = new();

    public AutonomousJobSession Create(ResearchJobQueueItem queueItem)
    {
        var session = new AutonomousJobSession(
            queueItem.JobId,
            queueItem.Identifier,
            queueItem.CorrelationId,
            queueItem.ExecutionMode,
            queueItem.ResolvedTicker);

        _sessions[queueItem.JobId] = session;
        return session;
    }

    public AutonomousJobSession? Get(Guid jobId) =>
        _sessions.TryGetValue(jobId, out var session) ? session : null;

    public void Remove(Guid jobId)
    {
        _sessions.TryRemove(jobId, out _);
    }
}
