using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Services;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;

namespace Proofsheet.Infrastructure.Providers;

public sealed class MockNewsProvider : INewsProvider
{
    private readonly MockResearchSeedCatalog _catalog;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;

    public MockNewsProvider(MockResearchSeedCatalog catalog, IJobExecutionContextAccessor jobExecutionContextAccessor)
    {
        _catalog = catalog;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
    }

    public MockNewsProvider(MockResearchSeedCatalog catalog)
        : this(catalog, new JobExecutionContextAccessor())
    {
    }

    public Task<ProviderResult<NewsCoverage>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seed = _catalog.GetOrGenerate(identifier, _jobExecutionContextAccessor.Current?.ResolvedTicker);
        return Task.FromResult(ProviderResult<NewsCoverage>.Success(seed.NewsCoverage, ProviderOrigin.Mock, "SeededMock"));
    }
}
