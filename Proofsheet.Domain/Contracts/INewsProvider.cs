using Proofsheet.Domain.Models;

namespace Proofsheet.Domain.Contracts;

public interface INewsProvider
{
    Task<ProviderResult<NewsCoverage>> GetAsync(string identifier, CancellationToken cancellationToken);
}
