using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Proofsheet.Api.Models;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Proofsheet.Tests.Integration.Api;

public sealed class ResearchJobsApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    [Fact]
    public async Task Submit_Poll_Result_Succeeds_For_Aapl()
    {
        await using var factory = new ProofsheetWebApplicationFactory();
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        var result = await WaitForCompletedResultAsync(client, accepted.JobId);

        Assert.Equal("Apple Inc.", result.Company);
        Assert.Equal("AAPL", result.Identifier);
        Assert.Equal(InvestmentSignal.Bullish, result.Signal);
        Assert.InRange(result.Confidence, 0, 100);
        Assert.InRange(result.Insights.Count, 2, 5);
        Assert.NotEmpty(result.Sources);
        Assert.Equal(result.Sources.Count, result.SourceDetails.Count);
        Assert.Equal(5, result.ProviderUsage.Count);
        Assert.Equal(result.Insights.Count, result.InsightDetails.Count);
        Assert.NotEmpty(result.Trace);
        Assert.True(IsSorted(result.Trace.Select(x => x.TimestampUtc)));
        Assert.All(result.InsightDetails, detail => Assert.All(detail.SourceIds, sourceId => Assert.Contains(sourceId, result.Sources)));
        Assert.All(result.ThesisDetails.SourceIds, sourceId => Assert.Contains(sourceId, result.Sources));
        Assert.All(result.ProviderUsage, usage => Assert.Equal(ProviderOrigin.Mock, usage.Origin));
        Assert.Contains(result.Trace, entry => entry.Summary.Contains("seeded mock provider", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.SourceDetails, detail => Assert.Equal(ProviderOrigin.Mock, detail.ProviderOrigin));
    }

    [Fact]
    public async Task Result_Returns_202_While_Job_Is_Running()
    {
        await using var factory = new ProofsheetWebApplicationFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<ICompanyProfileProvider>();
                services.AddScoped<ICompanyProfileProvider>(provider =>
                    new DelayedCompanyProfileProvider(
                        provider.GetRequiredService<Proofsheet.Infrastructure.Providers.MockResearchSeedCatalog>(),
                        TimeSpan.FromMilliseconds(500)));
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        using var response = await client.GetAsync($"/api/v1/research-jobs/{accepted.JobId}/result");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var pending = await response.Content.ReadFromJsonAsync<PendingResearchJobResultResponse>(JsonOptions);
        JobStatus[] pendingStatuses = [JobStatus.Queued, JobStatus.Running];
        Assert.Contains(pending!.Status, pendingStatuses);
    }

    [Fact]
    public async Task Status_Returns_Trace_Snapshot_While_Job_Is_Running()
    {
        await using var factory = new ProofsheetWebApplicationFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<ICompanyProfileProvider>();
                services.AddScoped<ICompanyProfileProvider>(provider =>
                    new DelayedCompanyProfileProvider(
                        provider.GetRequiredService<Proofsheet.Infrastructure.Providers.MockResearchSeedCatalog>(),
                        TimeSpan.FromMilliseconds(500)));
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        await WaitForRunningStatusAsync(client, accepted.JobId);

        using var response = await client.GetAsync($"/api/v1/research-jobs/{accepted.JobId}");
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<ResearchJobStatusResponse>(JsonOptions);

        Assert.NotNull(status);
        Assert.NotEmpty(status!.Trace);
        Assert.Contains(status.Trace, entry => entry.Step == WorkflowStep.IdentifierResolution);
        Assert.Contains(status.Trace, entry => entry.Step == WorkflowStep.ResearchPlanCreation);
    }

    [Fact]
    public async Task Failed_Job_Returns_409_When_Critical_Source_Is_Missing()
    {
        await using var factory = new ProofsheetWebApplicationFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IFinancialStatementProvider>();
                services.AddScoped<IFinancialStatementProvider, EmptyFinancialStatementProvider>();
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        var response = await WaitForTerminalResponseAsync(client, accepted.JobId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.Equal("JobFailed", problem!.Extensions["errorCode"]?.ToString());
    }

    [Fact]
    public async Task Unsupported_Identifier_Returns_422()
    {
        await using var factory = new ProofsheetWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/research-jobs", new SubmitResearchJobRequest { Identifier = "ZZZZZZZZ" });

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.Equal("UnsupportedIdentifier", problem!.Extensions["errorCode"]?.ToString());
    }

    [Fact]
    public async Task Unknown_Job_Returns_404()
    {
        await using var factory = new ProofsheetWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/research-jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Queue_Full_Returns_503()
    {
        await using var factory = new ProofsheetWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["ResearchJobs:QueueCapacity"] = "1",
                ["ResearchJobs:MaxConcurrentJobs"] = "1"
            },
            services =>
            {
                services.RemoveAll<ICompanyProfileProvider>();
                services.AddScoped<ICompanyProfileProvider>(provider =>
                    new DelayedCompanyProfileProvider(
                        provider.GetRequiredService<Proofsheet.Infrastructure.Providers.MockResearchSeedCatalog>(),
                        TimeSpan.FromMilliseconds(1500)));
            });
        using var client = factory.CreateClient();

        var first = await SubmitAsync(client, "AAPL");
        await WaitForRunningStatusAsync(client, first.JobId);
        var second = await SubmitAsync(client, "MSFT");

        using var response = await client.PostAsJsonAsync("/api/v1/research-jobs", new SubmitResearchJobRequest { Identifier = "NVDA" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0"));
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.Equal("QueueCapacityExceeded", problem!.Extensions["errorCode"]?.ToString());
        Assert.NotEqual(first.JobId, second.JobId);
    }

    [Fact]
    public async Task Tsla_Degraded_Scenario_Succeeds_With_Required_Warnings_And_Trace()
    {
        await using var factory = new ProofsheetWebApplicationFactory();
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "TSLA");
        var result = await WaitForCompletedResultAsync(client, accepted.JobId);

        Assert.Equal(InvestmentSignal.Neutral, result.Signal);
        Assert.Contains("Market snapshot required retry before successful retrieval.", result.Warnings);
        Assert.Contains("Low news coverage reduced narrative confidence.", result.Warnings);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.ProviderRetry);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.AdaptiveBranching);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.FinalReflection);
    }

    [Fact]
    public async Task Misconfigured_OpenAi_Falls_Back_With_Warning()
    {
        await using var factory = new ProofsheetWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["ResearchJobs:Mode"] = "Hybrid",
                ["OpenAiResponses:Enabled"] = "false"
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        var result = await WaitForCompletedResultAsync(client, accepted.JobId);

        Assert.Contains(result.Warnings, warning => warning.Contains("Deterministic synthesis was used instead.", StringComparison.Ordinal));
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.ProviderFallback);
    }

    [Fact]
    public async Task Misconfigured_TwelveData_Reports_Mock_Fallback_In_Result_And_Trace()
    {
        await using var factory = new ProofsheetWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["ResearchJobs:Mode"] = "Hybrid",
                ["TwelveData:Enabled"] = "true",
                ["TwelveData:ApiKey"] = ""
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        var result = await WaitForCompletedResultAsync(client, accepted.JobId);

        var marketUsage = Assert.Single(result.ProviderUsage, usage => usage.Domain == SourceType.MarketSnapshot);
        Assert.Equal(ProviderOrigin.Mock, marketUsage.Origin);
        Assert.True(marketUsage.FallbackOccurred);
        Assert.Contains("Twelve Data", marketUsage.FallbackReason, StringComparison.Ordinal);
        Assert.Contains(result.Trace, entry =>
            entry.Step == WorkflowStep.ProviderFallback &&
            entry.Summary.Contains("Market snapshot used seeded mock fallback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Autonomous_Job_Completes_With_Coordinator_And_Specialist_Trace()
    {
        var responsesApi = new FakeResponsesApi(
            JsonDocument.Parse("""
                {
                  "id": "resp_coord_1",
                  "output": [
                    { "type": "function_call", "call_id": "call_1", "name": "resolveTicker", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_2", "name": "getCompanyProfile", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_3", "name": "getMarketSnapshot", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_4", "name": "getFinancialStatements", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_5", "name": "getNewsCoverage", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_6", "name": "getFilingCoverage", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_7", "name": "getEvidenceStatus", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_8", "name": "handoffToSpecialist", "arguments": "{}" }
                  ]
                }
                """),
            JsonDocument.Parse("""
                {
                  "id": "resp_spec_1",
                  "output_text": "{\"thesis\":\"Apple retains resilient ecosystem economics while mixed near-term signals keep expectations disciplined.\",\"signal\":\"Bullish\",\"confidence\":86,\"insights\":[\"Financial execution remains strong.\",\"Services and ecosystem breadth continue to support resilience.\",\"Near-term market noise does not outweigh the current evidence.\"],\"thesisDetails\":{\"summary\":\"Thesis supported by financials, profile, and filings.\",\"sourceIds\":[\"financials:AAPL\",\"profile:AAPL\",\"filing:AAPL:001\"]},\"insightDetails\":[{\"insight\":\"Financial execution remains strong.\",\"sourceIds\":[\"financials:AAPL\"]},{\"insight\":\"Services and ecosystem breadth continue to support resilience.\",\"sourceIds\":[\"profile:AAPL\",\"news:AAPL:001\"]},{\"insight\":\"Near-term market noise does not outweigh the current evidence.\",\"sourceIds\":[\"financials:AAPL\",\"filing:AAPL:001\"]}],\"signalDetails\":{\"rationale\":\"Financials and profile context remain supportive overall.\",\"sourceIds\":[\"financials:AAPL\",\"profile:AAPL\"]},\"confidenceDetails\":{\"rationale\":\"Confidence stays solid because the evidence package is complete and well-cited.\",\"sourceIds\":[\"financials:AAPL\",\"filing:AAPL:001\"]}}"
                }
                """),
            JsonDocument.Parse("""
                {
                  "id": "resp_spec_critique",
                  "output_text": "{\"approved\":true,\"issues\":[]}"
                }
                """));

        await using var factory = new ProofsheetWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["OpenAiResponses:Enabled"] = "true",
                ["OpenAiResponses:ApiKey"] = "test-key",
                ["OpenAiResponses:CoordinatorModel"] = "gpt-5.4-nano",
                ["OpenAiResponses:SpecialistModel"] = "gpt-5.4-nano"
            },
            services =>
            {
                services.RemoveAll<IResponsesApi>();
                services.AddSingleton<IResponsesApi>(responsesApi);
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL", ResearchMode.Autonomous);
        var result = await WaitForCompletedResultAsync(client, accepted.JobId);

        Assert.Equal(ResearchMode.Autonomous, result.OrchestrationDetails.ResearchMode);
        Assert.True(result.OrchestrationDetails.HandoffOccurred);
        Assert.True(result.OrchestrationDetails.ToolCallCount >= 1);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.CoordinatorPlanning);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.EvidenceGapReview);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.SpecialistHandoff);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.SpecialistDraft);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.SpecialistCritique);
        Assert.Contains(result.Trace, entry => entry.Step == WorkflowStep.OutputValidation);
        Assert.Equal(5, result.ProviderUsage.Count);
    }

    private static async Task<ResearchJobAcceptedResponse> SubmitAsync(HttpClient client, string identifier, ResearchMode? researchMode = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/research-jobs", new SubmitResearchJobRequest { Identifier = identifier, ResearchMode = researchMode });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResearchJobAcceptedResponse>(JsonOptions))!;
    }

    private static async Task<CompletedResearchResult> WaitForCompletedResultAsync(HttpClient client, Guid jobId)
    {
        using var response = await WaitForTerminalResponseAsync(client, jobId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CompletedResearchResult>(JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> WaitForTerminalResponseAsync(HttpClient client, Guid jobId)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            var response = await client.GetAsync($"/api/v1/research-jobs/{jobId}/result");
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(50);
        }

        throw new TimeoutException("Timed out waiting for terminal job result.");
    }

    private static async Task WaitForRunningStatusAsync(HttpClient client, Guid jobId)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            using var response = await client.GetAsync($"/api/v1/research-jobs/{jobId}");
            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<ResearchJobStatusResponse>(JsonOptions);
            if (status!.Status == JobStatus.Running)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out waiting for job to enter the running state.");
    }

    private static bool IsSorted(IEnumerable<DateTimeOffset> values)
    {
        DateTimeOffset? previous = null;
        foreach (var value in values)
        {
            if (previous is not null && value < previous.Value)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class FakeResponsesApi : IResponsesApi
    {
        private readonly Queue<JsonDocument> _responses;

        public FakeResponsesApi(params JsonDocument[] responses)
        {
            _responses = new Queue<JsonDocument>(responses);
        }

        public Task<JsonDocument> CreateResponseAsync(JsonObject body, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(_responses.Dequeue());

        public Task<JsonDocument> CreateResponseStreamAsync(JsonObject body, int timeoutSeconds, Func<ReportChatProviderEvent, Task>? onStreamEvent, CancellationToken cancellationToken = default) =>
            Task.FromResult(_responses.Dequeue());
    }
}
