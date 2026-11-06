using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Services;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;

namespace Proofsheet.Infrastructure.Providers;

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
