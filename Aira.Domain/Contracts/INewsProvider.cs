using Aira.Domain.Models;

namespace Aira.Domain.Contracts;

public interface INewsProvider
{
    Task<ProviderResult<NewsCoverage>> GetAsync(string identifier, CancellationToken cancellationToken);
}
