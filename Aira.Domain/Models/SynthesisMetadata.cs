using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record SynthesisMetadata(
    ResearchExecutionMode Mode,
    string Model,
    int ValidationRetries,
    bool FallbackOccurred);
