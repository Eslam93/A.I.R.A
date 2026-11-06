using Proofsheet.Domain.Models;

namespace Proofsheet.Domain.Contracts;

public interface IFinancialStatementProvider
{
    Task<ProviderResult<FinancialStatement>> GetAsync(string identifier, CancellationToken cancellationToken);
}
