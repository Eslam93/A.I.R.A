using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Proofsheet.Application.Models;
using Proofsheet.Infrastructure.Chat;

namespace Proofsheet.Tests.Unit.Chat;

public sealed class ResponsesApiTests
{
    [Fact]
    public async Task CreateResponseStreamAsync_Streams_Deltas_And_Returns_The_Completed_Response()
    {
        var sse = string.Join(
            "\n",
            "event: response.output_text.delta",
            """data: {"type":"response.output_text.delta","response_id":"resp_1","delta":"Hel"}""",
            "",
            "event: response.output_text.delta",
            """data: {"type":"response.output_text.delta","response_id":"resp_1","delta":"lo"}""",
            "",
            "event: response.completed",
            """data: {"type":"response.completed","response":{"id":"resp_1","status":"completed"}}""",
            "");
        var api = CreateApi(sse);
        var events = new List<ReportChatProviderEvent>();

        using var completed = await api.CreateResponseStreamAsync(
            new JsonObject { ["model"] = "test-model" },
            timeoutSeconds: 10,
            streamEvent =>
            {
                events.Add(streamEvent);
                return Task.CompletedTask;
            });

        Assert.Equal("resp_1", completed.RootElement.GetProperty("id").GetString());
        Assert.Equal("Hello", string.Concat(events.Select(streamEvent => streamEvent.TextDelta)));
        string[] expectedTypes = ["response.output_text.delta", "response.output_text.delta", "response.completed"];
        Assert.Equal(expectedTypes, events.Select(streamEvent => streamEvent.Type).ToArray());
        Assert.All(events, streamEvent => Assert.Equal("resp_1", streamEvent.ResponseId));
    }

    [Fact]
    public async Task CreateResponseStreamAsync_Fails_When_The_Stream_Ends_Without_A_Completion_Event()
    {
        var sse = string.Join(
            "\n",
            "event: response.output_text.delta",
            """data: {"type":"response.output_text.delta","response_id":"resp_1","delta":"partial"}""",
            "");
        var api = CreateApi(sse);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => api.CreateResponseStreamAsync(new JsonObject(), timeoutSeconds: 10, onStreamEvent: null));

        Assert.Contains("ended before a completion event", exception.Message, StringComparison.Ordinal);
    }

    private static ResponsesApi CreateApi(string sseBody) =>
        new(new HttpClient(new StaticResponseHandler(sseBody)) { BaseAddress = new Uri("https://api.openai.test/") });

    private sealed class StaticResponseHandler(string sseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sseBody, Encoding.UTF8, "text/event-stream")
            });
    }
}
