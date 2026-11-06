using Proofsheet.Domain.Enums;

namespace Proofsheet.Domain.Models;

public sealed record SynthesisMetadata(
    ResearchExecutionMode Mode,
    string Model,
    int ValidationRetries,
    bool FallbackOccurred);
