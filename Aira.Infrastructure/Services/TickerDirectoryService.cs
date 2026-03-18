using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Infrastructure.Clients;
using Aira.Infrastructure.Providers;

namespace Aira.Infrastructure.Services;

public sealed class TickerDirectoryService : ITickerDirectoryService
{
    private readonly SecEdgarClient _secEdgarClient;
    private readonly MockResearchSeedCatalog _seedCatalog;

    public TickerDirectoryService(SecEdgarClient secEdgarClient, MockResearchSeedCatalog seedCatalog)
    {
        _secEdgarClient = secEdgarClient;
        _seedCatalog = seedCatalog;
    }

    public async Task<ResolvedTickerInfo?> ResolveAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var profile = await _secEdgarClient.GetTickerProfileAsync(identifier, cancellationToken);
        if (profile is not null)
        {
            return new ResolvedTickerInfo(profile.Identifier, profile.CompanyName, profile.Exchange, profile.CikPadded);
        }

        return _seedCatalog.TryGet(identifier, out var seed)
            ? new ResolvedTickerInfo(seed.CompanyProfile.Identifier, seed.CompanyProfile.CompanyName, seed.CompanyProfile.Exchange, seed.CompanyProfile.CikPadded)
            : null;
    }

    public async Task<IReadOnlyList<TickerSearchItemResponse>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        var items = await _secEdgarClient.SearchTickerProfilesAsync(query, limit, cancellationToken);
        if (items.Count > 0)
        {
            return items
                .Select(item => new TickerSearchItemResponse(item.Identifier, item.CompanyName, item.Exchange, item.CikPadded))
                .ToArray();
        }

        return SeedSearch(query, limit);
    }

    private IReadOnlyList<TickerSearchItemResponse> SeedSearch(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<TickerSearchItemResponse>();
        }

        var normalized = query.Trim();
        return new[] { "AAPL", "MSFT", "NVDA", "TSLA", "JPM", "AMZN", "GOOGL", "AMD", "XOM", "UNH" }
            .Select(symbol => _seedCatalog.TryGet(symbol, out var seed)
                ? new TickerSearchItemResponse(seed.CompanyProfile.Identifier, seed.CompanyProfile.CompanyName, seed.CompanyProfile.Exchange, seed.CompanyProfile.CikPadded)
                : null)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item =>
                item.Symbol.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                item.CompanyName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 20))
            .ToArray();
    }
}
