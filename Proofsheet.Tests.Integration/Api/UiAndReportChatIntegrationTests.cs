using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Proofsheet.Api.Models;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Proofsheet.Tests.Integration.Api;

public sealed class UiAndReportChatIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    [Fact]
    public async Task Home_And_Report_Pages_Render()
    {
        await using var factory = new ProofsheetWebApplicationFactory();
        using var client = factory.CreateClient();

        var home = await client.GetStringAsync("/");
        Assert.Contains("Generate an investment report", home, StringComparison.OrdinalIgnoreCase);

        var accepted = await SubmitAsync(client, "AAPL");
        var reportPage = await client.GetStringAsync($"/reports/{accepted.JobId}");
        Assert.Contains("Progress", reportPage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chat", reportPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completed_Job_Can_Stream_Report_Chat_And_Replay_Stream_Events()
    {
        var stub = new StubReportChatService(StubChatMode.Complete);
        await using var factory = new ProofsheetWebApplicationFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IReportChatService>();
                services.AddSingleton<IReportChatService>(stub);
                services.RemoveAll<IReportChatAvailabilityService>();
                services.AddSingleton<IReportChatAvailabilityService>(new AlwaysEnabledReportChatAvailabilityService());
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        await WaitForCompletedResultAsync(client, accepted.JobId);

        await using var connection = BuildConnection(factory, client.BaseAddress!);
        var receivedEvents = new ConcurrentQueue<ReportChatStreamEventResponse>();
        var chatMessageSource = new TaskCompletionSource<ReportChatMessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<ReportChatStreamEventResponse>("ReceiveStreamEvent", evt => receivedEvents.Enqueue(evt));
        connection.On<ReportChatMessageResponse>("ReceiveChatMessage", message => chatMessageSource.TrySetResult(message));

        await connection.StartAsync();

        var thread = await connection.InvokeAsync<ReportChatThreadCreatedResponse>("CreateThread", accepted.JobId);
        await connection.InvokeAsync("SendMessage", thread.ThreadId, "Why is the report bullish?");
        var finalMessage = await chatMessageSource.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(thread.JobId, finalMessage.JobId);
        Assert.Contains("financials:AAPL", finalMessage.CitedSourceIds);
        Assert.Contains(receivedEvents, evt => evt.Kind == "delta");
        Assert.Contains(receivedEvents, evt => evt.Kind == "done");

        var replay = await connection.InvokeAsync<ReportChatStreamResumeResponse>("ResumeStream", thread.ThreadId, 0L);
        Assert.False(replay.InProgress);
        Assert.NotEmpty(replay.Events);
    }

    [Fact]
    public async Task CreateThread_Rejects_NonCompleted_Job()
    {
        await using var factory = new ProofsheetWebApplicationFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IReportChatAvailabilityService>();
                services.AddSingleton<IReportChatAvailabilityService>(new AlwaysEnabledReportChatAvailabilityService());
                services.RemoveAll<ICompanyProfileProvider>();
                services.AddScoped<ICompanyProfileProvider>(provider =>
                    new DelayedCompanyProfileProvider(
                        provider.GetRequiredService<Proofsheet.Infrastructure.Providers.MockResearchSeedCatalog>(),
                        TimeSpan.FromMilliseconds(500)));
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");

        await using var connection = BuildConnection(factory, client.BaseAddress!);
        await connection.StartAsync();

        var exception = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync<ReportChatThreadCreatedResponse>("CreateThread", accepted.JobId));

        Assert.Contains("completed research jobs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelStream_Stops_Active_Report_Chat()
    {
        var stub = new StubReportChatService(StubChatMode.Cancelable);
        await using var factory = new ProofsheetWebApplicationFactory(
            configureTestServices: services =>
            {
                services.RemoveAll<IReportChatService>();
                services.AddSingleton<IReportChatService>(stub);
                services.RemoveAll<IReportChatAvailabilityService>();
                services.AddSingleton<IReportChatAvailabilityService>(new AlwaysEnabledReportChatAvailabilityService());
            });
        using var client = factory.CreateClient();

        var accepted = await SubmitAsync(client, "AAPL");
        await WaitForCompletedResultAsync(client, accepted.JobId);

        await using var senderConnection = BuildConnection(factory, client.BaseAddress!);
        await using var controlConnection = BuildConnection(factory, client.BaseAddress!);
        var canceledSource = new TaskCompletionSource<ReportChatStreamEventResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        controlConnection.On<ReportChatStreamEventResponse>("ReceiveStreamEvent", evt =>
        {
            if (evt.Kind == "canceled")
            {
                canceledSource.TrySetResult(evt);
            }
        });

        await senderConnection.StartAsync();
        await controlConnection.StartAsync();
        var thread = await senderConnection.InvokeAsync<ReportChatThreadCreatedResponse>("CreateThread", accepted.JobId);

        var sendTask = senderConnection.InvokeAsync("SendMessage", thread.ThreadId, "Explain the report.");
        await stub.WaitUntilStartedAsync();
        await controlConnection.InvokeAsync<bool>("CancelStream", thread.ThreadId);

        var canceled = await canceledSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sendTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("canceled", canceled.Kind);
        Assert.Contains("canceled", canceled.ProviderEventType, StringComparison.OrdinalIgnoreCase);
    }

    private static HubConnection BuildConnection(ProofsheetWebApplicationFactory factory, Uri baseAddress)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(baseAddress, "/hubs/report-chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();
    }

    private static async Task<ResearchJobAcceptedResponse> SubmitAsync(HttpClient client, string identifier)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/research-jobs", new SubmitResearchJobRequest { Identifier = identifier });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResearchJobAcceptedResponse>(JsonOptions))!;
    }

    private static async Task<CompletedResearchResult> WaitForCompletedResultAsync(HttpClient client, Guid jobId)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            using var response = await client.GetAsync($"/api/v1/research-jobs/{jobId}/result");
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return (await response.Content.ReadFromJsonAsync<CompletedResearchResult>(JsonOptions))!;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Timed out waiting for completed result.");
    }

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private enum StubChatMode
    {
        Complete,
        Cancelable
    }

    private sealed class StubReportChatService : IReportChatService
    {
        private readonly StubChatMode _mode;
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StubReportChatService(StubChatMode mode)
        {
            _mode = mode;
        }

        public async Task<ReportChatExecutionResult> SendMessageAsync(
            CompletedResearchResult reportSnapshot,
            string userMessage,
            string previousResponseId,
            Func<ReportChatProviderEvent, Task>? onProviderEvent = null,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult(true);

            if (onProviderEvent is not null)
            {
                await onProviderEvent(new ReportChatProviderEvent("response.output_text.delta", "resp_stream", "The thesis leans bullish because ", "{}"));
                await onProviderEvent(new ReportChatProviderEvent("response.output_text.delta", "resp_stream", "financials:AAPL remains strong.", "{}"));
            }

            if (_mode == StubChatMode.Cancelable)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return new ReportChatExecutionResult(
                string.IsNullOrWhiteSpace(previousResponseId) ? "resp_test_1" : "resp_test_2",
                "The report is bullish because the financial statement evidence is strong [financials:AAPL].",
                ["financials:AAPL"]);
        }

        public Task WaitUntilStartedAsync() => _started.Task;
    }

    private sealed class AlwaysEnabledReportChatAvailabilityService : IReportChatAvailabilityService
    {
        public bool IsConfigured => true;

        public string UnavailableReason => string.Empty;
    }
}
