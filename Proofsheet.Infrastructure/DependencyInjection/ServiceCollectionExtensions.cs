using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Application.Services;
using Proofsheet.Domain.Contracts;
using Proofsheet.Infrastructure.Clients;
using Proofsheet.Infrastructure.Chat;
using Proofsheet.Infrastructure.Autonomous;
using Proofsheet.Infrastructure.Execution;
using Proofsheet.Infrastructure.Options;
using Proofsheet.Infrastructure.Persistence;
using Proofsheet.Infrastructure.Providers;
using Proofsheet.Infrastructure.Queueing;
using Proofsheet.Infrastructure.Reasoning;
using Proofsheet.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net;

namespace Proofsheet.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProofsheetBackend(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ResearchExecutionOptions>(configuration.GetSection(ResearchExecutionOptions.SectionName));
        services.Configure<OpenAiResponsesOptions>(configuration.GetSection(OpenAiResponsesOptions.SectionName));
        services.Configure<SecEdgarOptions>(configuration.GetSection(SecEdgarOptions.SectionName));
        services.Configure<TwelveDataOptions>(configuration.GetSection(TwelveDataOptions.SectionName));
        services.Configure<AlphaVantageOptions>(configuration.GetSection(AlphaVantageOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<IResearchJobStore, InMemoryResearchJobStore>();
        services.AddSingleton<IAutonomousJobSessionStore, InMemoryAutonomousJobSessionStore>();
        services.AddSingleton<IReportChatThreadStore, InMemoryReportChatThreadStore>();
        services.AddSingleton<IResearchJobQueue, ChannelResearchJobQueue>();
        services.AddSingleton<IReportChatAvailabilityService, OpenAiResponsesReportChatAvailabilityService>();
        services.AddSingleton<IResearchExecutionModeResolver, ResearchExecutionModeResolver>();
        services.AddSingleton<ITickerDirectoryService, TickerDirectoryService>();

        services.AddSingleton<IIdentifierValidator, IdentifierValidator>();
        services.AddSingleton<IResearchJobSubmissionService, ResearchJobSubmissionService>();
        services.AddSingleton<IResearchJobQueryService, ResearchJobQueryService>();
        services.AddSingleton<IReportChatPromptProvider, FileReportChatPromptProvider>();
        services.AddSingleton<IReportSynthesisPromptProvider, FileReportSynthesisPromptProvider>();
        services.AddSingleton<ICoordinatorAgentPromptProvider, FileCoordinatorAgentPromptProvider>();
        services.AddSingleton<ISpecialistAgentPromptProvider, FileSpecialistAgentPromptProvider>();

        services.AddScoped<IJobExecutionContextAccessor, JobExecutionContextAccessor>();
        services.AddScoped<IResearchJobProcessor, ResearchJobProcessor>();
        services.AddScoped<IAutonomousResearchOrchestrator, AutonomousResearchOrchestrator>();
        services.AddScoped<ICoordinatorAgentService, ResponsesCoordinatorAgentService>();
        services.AddScoped<IStockReportSpecialistService, ResponsesStockReportSpecialistService>();
        services.AddScoped<IResearchToolRegistry, ResearchToolRegistry>();
        services.AddScoped<IReportChatOrchestrator, ReportChatOrchestrator>();

        services.AddSingleton<MockResearchSeedCatalog>();
        services.AddScoped<MockCompanyProfileProvider>();
        services.AddScoped<MockMarketSnapshotProvider>();
        services.AddScoped<MockFinancialStatementProvider>();
        services.AddScoped<MockNewsProvider>();
        services.AddScoped<MockFilingExcerptProvider>();

        services.AddHttpClient<SecEdgarClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecEdgarOptions>>().Value;
                client.BaseAddress = new Uri(EnsureTrailingSlash(options.DataBaseUrl));
                if (!string.IsNullOrWhiteSpace(options.UserAgent))
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
                }
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
        services.AddHttpClient<HybridMarketSnapshotProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TwelveDataOptions>>().Value;
            client.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
        });
        services.AddHttpClient<HybridNewsProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AlphaVantageOptions>>().Value;
            client.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
        });

        services.AddScoped<HybridCompanyProfileProvider>();
        services.AddScoped<HybridFinancialStatementProvider>();
        services.AddScoped<HybridFilingExcerptProvider>();

        services.AddScoped<ICompanyProfileProvider>(serviceProvider => serviceProvider.GetRequiredService<HybridCompanyProfileProvider>());
        services.AddScoped<IMarketSnapshotProvider>(serviceProvider => serviceProvider.GetRequiredService<HybridMarketSnapshotProvider>());
        services.AddScoped<IFinancialStatementProvider>(serviceProvider => serviceProvider.GetRequiredService<HybridFinancialStatementProvider>());
        services.AddScoped<INewsProvider>(serviceProvider => serviceProvider.GetRequiredService<HybridNewsProvider>());
        services.AddScoped<IFilingExcerptProvider>(serviceProvider => serviceProvider.GetRequiredService<HybridFilingExcerptProvider>());

        services.AddSingleton<DeterministicResearchReasoner>();
        services.AddScoped<OpenAiResearchReasoner>();
        services.AddHttpClient<IResponsesApi, ResponsesApi>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAiResponsesOptions>>().Value;
            client.BaseAddress = new Uri(EnsureTrailingSlash(string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.openai.com" : options.BaseUrl));
            client.Timeout = Timeout.InfiniteTimeSpan;
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            }
        });
        services.AddScoped<IReportChatService, ResponsesReportChatService>();
        services.AddScoped<IResearchReasonerSelector, ConfiguredResearchReasonerSelector>();

        services.AddHostedService<ResearchJobWorkerHostedService>();
        return services;
    }

    private static string EnsureTrailingSlash(string baseUrl) =>
        baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";
}
