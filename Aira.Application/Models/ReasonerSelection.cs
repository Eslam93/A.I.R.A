using Aira.Domain.Contracts;

namespace Aira.Application.Models;

public sealed record ReasonerSelection(
    IResearchReasoner? Reasoner,
    string ModelName,
    bool FallbackOccurred,
    string? WarningMessage,
    string? FailureMessage)
{
    public bool IsAvailable => Reasoner is not null;
}
