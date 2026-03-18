using Aira.Application.Abstractions;
using Aira.Application.Services;
using Aira.Domain.Contracts;
using Aira.Domain.Enums;
using Aira.Domain.Models;

namespace Aira.Infrastructure.Providers;

public sealed class MockFilingExcerptProvider : IFilingExcerptProvider
{
    private readonly MockResearchSeedCatalog _catalog;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;

    public MockFilingExcerptProvider(MockResearchSeedCatalog catalog, IJobExecutionContextAccessor jobExecutionContextAccessor)
    {
        _catalog = catalog;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
    }

    public MockFilingExcerptProvider(MockResearchSeedCatalog catalog)
        : this(catalog, new JobExecutionContextAccessor())
    {
    }

    public Task<ProviderResult<FilingCoverage>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seed = _catalog.GetOrGenerate(identifier, _jobExecutionContextAccessor.Current?.ResolvedTicker);
        return Task.FromResult(ProviderResult<FilingCoverage>.Success(seed.FilingCoverage, ProviderOrigin.Mock, "SeededMock"));
    }
}
