using Aira.Domain.Enums;

namespace Aira.Application.Abstractions;

public interface IResearchExecutionModeResolver
{
    ResearchExecutionMode Resolve();
}
