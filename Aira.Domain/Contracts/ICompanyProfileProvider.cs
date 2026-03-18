using Aira.Domain.Models;

namespace Aira.Domain.Contracts;

public interface ICompanyProfileProvider
{
    Task<ProviderResult<CompanyProfile>> GetAsync(string identifier, CancellationToken cancellationToken);
}
