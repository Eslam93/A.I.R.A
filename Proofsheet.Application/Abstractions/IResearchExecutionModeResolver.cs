using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Abstractions;

public interface IResearchExecutionModeResolver
{
    ResearchExecutionMode Resolve();
}
