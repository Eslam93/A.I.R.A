using Proofsheet.Domain.Models;

namespace Proofsheet.Domain.Contracts;

public interface IFilingExcerptProvider
{
    Task<ProviderResult<FilingCoverage>> GetAsync(string identifier, CancellationToken cancellationToken);
}
