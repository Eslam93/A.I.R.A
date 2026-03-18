using Aira.Domain.Models;

namespace Aira.Domain.Contracts;

public interface IFinancialStatementProvider
{
    Task<ProviderResult<FinancialStatement>> GetAsync(string identifier, CancellationToken cancellationToken);
}
