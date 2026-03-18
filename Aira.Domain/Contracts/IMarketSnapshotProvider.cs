using Aira.Domain.Models;

namespace Aira.Domain.Contracts;

public interface IMarketSnapshotProvider
{
    Task<ProviderResult<MarketSnapshot>> GetAsync(string identifier, CancellationToken cancellationToken);
}
