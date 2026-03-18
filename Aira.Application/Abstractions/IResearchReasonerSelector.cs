using Aira.Application.Models;
using Aira.Domain.Enums;

namespace Aira.Application.Abstractions;

public interface IResearchReasonerSelector
{
    ReasonerSelection Select(ResearchExecutionMode executionMode);
}
