using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record ResearchAssessment(
    bool LowNewsMode,
    IReadOnlyDictionary<SourceType, decimal> ActiveWeights,
    int CompletenessScore,
    int EvidenceScore,
    int DirectionalScore,
    decimal ContradictionScore,
    int AgreementScore,
    bool UnresolvedContradiction,
    int RetryCount,
    int MissingNonCriticalSourceCount,
    bool OpenAiFallbackOccurred);
