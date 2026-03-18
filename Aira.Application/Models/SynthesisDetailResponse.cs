using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed record SynthesisDetailResponse(
    ResearchExecutionMode Mode,
    string Model,
    int ValidationRetries,
    bool FallbackOccurred);
