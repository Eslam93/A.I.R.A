using Aira.Application.Abstractions;
using Aira.Application.Services;
using Aira.Domain.Contracts;
using Aira.Domain.Enums;
using Aira.Domain.Models;

namespace Aira.Infrastructure.Providers;

public sealed class MockCompanyProfileProvider : ICompanyProfileProvider
{
    private readonly MockResearchSeedCatalog _catalog;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;

    public MockCompanyProfileProvider(MockResearchSeedCatalog catalog, IJobExecutionContextAccessor jobExecutionContextAccessor)
    {
        _catalog = catalog;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
    }

    public MockCompanyProfileProvider(MockResearchSeedCatalog catalog)
        : this(catalog, new JobExecutionContextAccessor())
    {
    }

    public Task<ProviderResult<CompanyProfile>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seed = _catalog.GetOrGenerate(identifier, _jobExecutionContextAccessor.Current?.ResolvedTicker);
        return Task.FromResult(ProviderResult<CompanyProfile>.Success(seed.CompanyProfile, ProviderOrigin.Mock, "SeededMock"));
    }
}
