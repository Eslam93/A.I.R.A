using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface ITickerDirectoryService
{
    Task<ResolvedTickerInfo?> ResolveAsync(string identifier, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TickerSearchItemResponse>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default);
}
