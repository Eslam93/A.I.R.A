using Aira.Domain.Models;

namespace Aira.Domain.Contracts;

public interface IFilingExcerptProvider
{
    Task<ProviderResult<FilingCoverage>> GetAsync(string identifier, CancellationToken cancellationToken);
}
