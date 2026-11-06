using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Abstractions;

public interface IResearchJobSubmissionService
{
    Task<ResearchJobSubmissionResult> SubmitAsync(string? identifier, ResearchMode? researchMode, CancellationToken cancellationToken);
}
