using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Abstractions;

public interface IResearchReasonerSelector
{
    ReasonerSelection Select(ResearchExecutionMode executionMode);
}
