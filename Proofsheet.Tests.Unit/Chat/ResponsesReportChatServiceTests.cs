using System.Text.Json;
using System.Text.Json.Nodes;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;
using Proofsheet.Infrastructure.Chat;
using Proofsheet.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Proofsheet.Tests.Unit.Chat;

public sealed class ResponsesReportChatServiceTests
{
    [Fact]
    public async Task SendMessageAsync_Uses_PreviousResponseId_After_Tool_Round_And_Maps_Citations()
    {
        var responsesApi = new FakeResponsesApi(
            CreateFunctionCallResponse("resp_1", "call_1", "getProviderUsage", "{}"),
            CreateMessageResponse("resp_2", "The report stays bullish based on financials:AAPL and filing:AAPL:001."));

        var service = new ResponsesReportChatService(
            responsesApi,
            new StaticPromptProvider("You are Proofsheet Report Chat."),
            Options.Create(new OpenAiResponsesOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                Model = "gpt-5-mini",
                EnableStreaming = false,
                MaxToolRounds = 2
            }));

        var result = await service.SendMessageAsync(CreateReport(), "Why is the report bullish?", string.Empty);

        Assert.Equal("resp_2", result.ResponseId);
        Assert.Contains("financials:AAPL", result.CitedSourceIds);
        Assert.Contains("filing:AAPL:001", result.CitedSourceIds);
        Assert.Equal(2, responsesApi.Bodies.Count);
        Assert.False(responsesApi.Bodies[0].ContainsKey("previous_response_id"));
        Assert.Equal("resp_1", responsesApi.Bodies[1]["previous_response_id"]!.GetValue<string>());

        var toolOutputs = Assert.IsType<JsonArray>(responsesApi.Bodies[1]["input"]);
        var toolResult = Assert.IsType<JsonObject>(toolOutputs[0]);
        Assert.Equal("function_call_output", toolResult["type"]!.GetValue<string>());
        Assert.Equal("call_1", toolResult["call_id"]!.GetValue<string>());
    }

    private static CompletedResearchResult CreateReport()
    {
        return new CompletedResearchResult(
            "Apple Inc.",
            "AAPL",
            "Apple has a resilient ecosystem.",
            InvestmentSignal.Bullish,
            92,
            new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero),
            ["Strong services momentum.", "Balance sheet remains supportive."],
            ["financials:AAPL", "filing:AAPL:001"],
            [],
            [
                new ResearchTraceEvent(WorkflowStep.IdentifierResolution, TraceStatus.Completed, "Identifier resolved.", [], new DateTimeOffset(2026, 3, 18, 12, 0, 1, TimeSpan.Zero)),
                new ResearchTraceEvent(WorkflowStep.Completion, TraceStatus.Completed, "Completed.", [], new DateTimeOffset(2026, 3, 18, 12, 0, 2, TimeSpan.Zero))
            ],
            [
                new SourceDetailResponse("financials:AAPL", SourceType.FinancialStatement, "Apple financials", new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero), TimestampType.AsOf, 0.92m, null, ProviderOrigin.Mock, "SeededMock", false),
                new SourceDetailResponse("filing:AAPL:001", SourceType.FilingExcerpt, "Apple filing", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), TimestampType.Published, 0.85m, null, ProviderOrigin.Mock, "SeededMock", false)
            ],
            [
                new ProviderUsageResponse(SourceType.FinancialStatement, ProviderResultStatus.Success, ProviderOrigin.Mock, "SeededMock", false, null),
                new ProviderUsageResponse(SourceType.FilingExcerpt, ProviderResultStatus.Success, ProviderOrigin.Mock, "SeededMock", false, null)
            ],
            [
                new InsightDetailResponse("Strong services momentum.", ["financials:AAPL"]),
                new InsightDetailResponse("Balance sheet remains supportive.", ["filing:AAPL:001"])
            ],
            new ThesisDetailResponse("Thesis supported by financials and filings.", ["financials:AAPL", "filing:AAPL:001"]));
    }

    private static JsonDocument CreateFunctionCallResponse(string id, string callId, string name, string arguments)
    {
        return JsonDocument.Parse($$"""
        {
          "id": "{{id}}",
          "output": [
            {
              "type": "function_call",
              "call_id": "{{callId}}",
              "name": "{{name}}",
              "arguments": {{JsonSerializer.Serialize(arguments)}}
            }
          ]
        }
        """);
    }

    private static JsonDocument CreateMessageResponse(string id, string text)
    {
        return JsonDocument.Parse($$"""
        {
          "id": "{{id}}",
          "output": [
            {
              "type": "message",
              "content": [
                {
                  "type": "output_text",
                  "text": {{JsonSerializer.Serialize(text)}}
                }
              ]
            }
          ]
        }
        """);
    }

    private sealed class StaticPromptProvider : IReportChatPromptProvider
    {
        private readonly string _prompt;

        public StaticPromptProvider(string prompt)
        {
            _prompt = prompt;
        }

        public string GetPrompt() => _prompt;
    }

    private sealed class FakeResponsesApi : IResponsesApi
    {
        private readonly Queue<JsonDocument> _responses;

        public FakeResponsesApi(params JsonDocument[] responses)
        {
            _responses = new Queue<JsonDocument>(responses);
        }

        public List<JsonObject> Bodies { get; } = new();

        public Task<JsonDocument> CreateResponseAsync(JsonObject body, int timeoutSeconds, CancellationToken cancellationToken = default)
        {
            Bodies.Add(Clone(body));
            return Task.FromResult(_responses.Dequeue());
        }

        public Task<JsonDocument> CreateResponseStreamAsync(JsonObject body, int timeoutSeconds, Func<ReportChatProviderEvent, Task>? onStreamEvent, CancellationToken cancellationToken = default)
        {
            Bodies.Add(Clone(body));
            return Task.FromResult(_responses.Dequeue());
        }

        private static JsonObject Clone(JsonObject body) => JsonNode.Parse(body.ToJsonString())!.AsObject();
    }
}
