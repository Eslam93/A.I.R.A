using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IResearchJobStore
{
    bool TryAdd(ResearchJobRecord record);

    bool TryRemove(Guid jobId);

    ResearchJobRecord? Get(Guid jobId);

    bool TryUpdate(Guid jobId, Func<ResearchJobRecord, ResearchJobRecord> update);
}
