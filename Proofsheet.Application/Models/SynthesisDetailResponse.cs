using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Models;

public sealed record SynthesisDetailResponse(
    ResearchExecutionMode Mode,
    string Model,
    int ValidationRetries,
    bool FallbackOccurred);
