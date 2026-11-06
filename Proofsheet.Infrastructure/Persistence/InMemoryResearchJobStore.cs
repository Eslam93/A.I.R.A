using System.Collections.Concurrent;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;

namespace Proofsheet.Infrastructure.Persistence;

public sealed class InMemoryResearchJobStore : IResearchJobStore
{
    private readonly ConcurrentDictionary<Guid, ResearchJobRecord> _records = new();

    public bool TryAdd(ResearchJobRecord record) => _records.TryAdd(record.JobId, record);

    public bool TryRemove(Guid jobId) => _records.TryRemove(jobId, out _);

    public ResearchJobRecord? Get(Guid jobId) =>
        _records.TryGetValue(jobId, out var record) ? record : null;

    public bool TryUpdate(Guid jobId, Func<ResearchJobRecord, ResearchJobRecord> update)
    {
        while (true)
        {
            if (!_records.TryGetValue(jobId, out var current))
            {
                return false;
            }

            var next = update(current);
            if (_records.TryUpdate(jobId, next, current))
            {
                return true;
            }
        }
    }
}
