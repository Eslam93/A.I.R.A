using Aira.Domain.Models;

namespace Aira.Domain.Contracts;

public interface IResearchReasoner
{
    Task<ReasoningResult> SynthesizeAsync(ResearchEvidence evidence, CancellationToken cancellationToken);
}
