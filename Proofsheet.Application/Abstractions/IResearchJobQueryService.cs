using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IResearchJobQueryService
{
    ResearchJobRecord? Get(Guid jobId);
}
