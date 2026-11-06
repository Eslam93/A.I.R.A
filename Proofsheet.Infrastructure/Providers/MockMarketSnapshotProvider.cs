using Proofsheet.Application.Abstractions;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;

namespace Proofsheet.Infrastructure.Providers;

public sealed class MockMarketSnapshotProvider : IMarketSnapshotProvider
{
    private readonly MockResearchSeedCatalog _catalog;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;

    public MockMarketSnapshotProvider(MockResearchSeedCatalog catalog, IJobExecutionContextAccessor jobExecutionContextAccessor)
    {
        _catalog = catalog;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
    }

    public Task<ProviderResult<MarketSnapshot>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var seed = _catalog.GetOrGenerate(identifier, _jobExecutionContextAccessor.Current?.ResolvedTicker);

        if (identifier.Equals("TSLA", StringComparison.OrdinalIgnoreCase) &&
            _jobExecutionContextAccessor.Current?.ConsumeTransientFailure("market:TSLA") == true)
        {
            return Task.FromResult(ProviderResult<MarketSnapshot>.TransientFailure("Market snapshot timed out.", true, ProviderOrigin.Mock, "SeededMock"));
        }

        return Task.FromResult(ProviderResult<MarketSnapshot>.Success(seed.MarketSnapshot, ProviderOrigin.Mock, "SeededMock"));
    }
}
