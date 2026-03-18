namespace Aira.Domain.Models;

public sealed record ResearchScoringInput(
    CompanyProfile? CompanyProfile,
    MarketSnapshot? MarketSnapshot,
    FinancialStatement? FinancialStatement,
    NewsCoverage? NewsCoverage,
    FilingCoverage? FilingCoverage,
    int RetryCount,
    int MissingNonCriticalSourceCount,
    bool OpenAiFallbackOccurred);
