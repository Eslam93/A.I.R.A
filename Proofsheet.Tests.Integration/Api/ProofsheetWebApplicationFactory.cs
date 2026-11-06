using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;
using Proofsheet.Infrastructure.Providers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Proofsheet.Tests.Integration.Api;

internal sealed class ProofsheetWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _configurationOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<IServiceCollection>? _configureTestServices;

    public ProofsheetWebApplicationFactory(
        IDictionary<string, string?>? configurationOverrides = null,
        Action<IServiceCollection>? configureTestServices = null)
    {
        _configurationOverrides["ResearchJobs:Mode"] = "Mock";
        _configurationOverrides["OpenAiResponses:Enabled"] = "false";
        _configurationOverrides["SecEdgar:Enabled"] = "false";
        _configurationOverrides["TwelveData:Enabled"] = "false";
        _configurationOverrides["AlphaVantage:Enabled"] = "false";

        if (configurationOverrides is not null)
        {
            foreach (var pair in configurationOverrides)
            {
                _configurationOverrides[pair.Key] = pair.Value;
            }
        }

        _configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(_configurationOverrides);
        });

        if (_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }
}

internal sealed class DelayedCompanyProfileProvider : ICompanyProfileProvider
{
    private readonly MockResearchSeedCatalog _catalog;
    private readonly TimeSpan _delay;

    public DelayedCompanyProfileProvider(MockResearchSeedCatalog catalog, TimeSpan delay)
    {
        _catalog = catalog;
        _delay = delay;
    }

    public async Task<ProviderResult<CompanyProfile>> GetAsync(string identifier, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);
        return _catalog.TryGet(identifier, out var seed)
            ? ProviderResult<CompanyProfile>.Success(seed.CompanyProfile, ProviderOrigin.Mock, "SeededMock")
            : ProviderResult<CompanyProfile>.Empty(origin: ProviderOrigin.Mock, providerName: "SeededMock");
    }
}

internal sealed class EmptyFinancialStatementProvider : IFinancialStatementProvider
{
    public Task<ProviderResult<FinancialStatement>> GetAsync(string identifier, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderResult<FinancialStatement>.Empty(origin: ProviderOrigin.Mock, providerName: "SeededMock"));
}
