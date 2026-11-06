using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Models;

public sealed record OrchestrationDetailResponse(
    ResearchMode ResearchMode,
    string? CoordinatorModel,
    string? SpecialistModel,
    bool HandoffOccurred,
    int ToolCallCount,
    int ReflectionRounds,
    int ValidationRetries);
