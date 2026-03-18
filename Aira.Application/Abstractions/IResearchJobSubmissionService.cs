using Aira.Application.Models;
using Aira.Domain.Enums;

namespace Aira.Application.Abstractions;

public interface IResearchJobSubmissionService
{
    Task<ResearchJobSubmissionResult> SubmitAsync(string? identifier, ResearchMode? researchMode, CancellationToken cancellationToken);
}
