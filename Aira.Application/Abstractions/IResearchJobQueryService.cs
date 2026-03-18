using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IResearchJobQueryService
{
    ResearchJobRecord? Get(Guid jobId);
}
