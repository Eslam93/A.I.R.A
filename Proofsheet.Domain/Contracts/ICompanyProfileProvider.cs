using Proofsheet.Domain.Models;

namespace Proofsheet.Domain.Contracts;

public interface ICompanyProfileProvider
{
    Task<ProviderResult<CompanyProfile>> GetAsync(string identifier, CancellationToken cancellationToken);
}
