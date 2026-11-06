using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Services;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;

namespace Proofsheet.Infrastructure.Providers;

public sealed class MockFinancialStatementProvider : IFinancialStatementProvider
{
    private readonly MockResearchSeedCatalog _catalog;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;

    public MockFinancialStatementProvider(MockResearchSeedCatalog catalog, IJobExecutionContextAccessor jobExecutionContextAccessor)
    {
        _catalog = catalog;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
    }

    public MockFinancialStatementProvider(MockResearchSeedCatalog catalog)
        : this(catalog, new JobExecutionContextAccessor())
    {
    }

    public Task<ProviderResult<FinancialStatement>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seed = _catalog.GetOrGenerate(identifier, _jobExecutionContextAccessor.Current?.ResolvedTicker);
        return Task.FromResult(ProviderResult<FinancialStatement>.Success(seed.FinancialStatement, ProviderOrigin.Mock, "SeededMock"));
    }
}
