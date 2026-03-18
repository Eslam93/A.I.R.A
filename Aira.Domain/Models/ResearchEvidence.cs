using Aira.Domain.Enums;

namespace Aira.Domain.Models;

public sealed record ResearchEvidence(
    string Identifier,
    ResearchExecutionMode ExecutionMode,
    CompanyProfile CompanyProfile,
    MarketSnapshot? MarketSnapshot,
    FinancialStatement FinancialStatement,
    NewsCoverage? NewsCoverage,
    FilingCoverage? FilingCoverage,
    ResearchAssessment Assessment,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, SourceDocument> SourceCatalog)
{
    public ResearchEvidence(
        string identifier,
        CompanyProfile companyProfile,
        MarketSnapshot? marketSnapshot,
        FinancialStatement financialStatement,
        NewsCoverage? newsCoverage,
        FilingCoverage? filingCoverage,
        ResearchAssessment assessment,
        IReadOnlyList<string> warnings,
        IReadOnlyDictionary<string, SourceDocument> sourceCatalog)
        : this(
            identifier,
            ResearchExecutionMode.Mock,
            companyProfile,
            marketSnapshot,
            financialStatement,
            newsCoverage,
            filingCoverage,
            assessment,
            warnings,
            sourceCatalog)
    {
    }
}
