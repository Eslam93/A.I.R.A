using Aira.Application.Abstractions;
using Aira.Application.Models;

namespace Aira.Application.Services;

public sealed class ResearchJobQueryService : IResearchJobQueryService
{
    private readonly IResearchJobStore _jobStore;

    public ResearchJobQueryService(IResearchJobStore jobStore)
    {
        _jobStore = jobStore;
    }

    public ResearchJobRecord? Get(Guid jobId) => _jobStore.Get(jobId);
}
