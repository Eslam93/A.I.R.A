using System.Text.Json;
using System.Text.Json.Nodes;
using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Domain.Enums;
using Aira.Infrastructure.Autonomous;
using Aira.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Aira.Tests.Unit.Autonomous;

public sealed class ResponsesCoordinatorAgentServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Uses_Only_Approved_Tool_Catalog_And_Returns_Handoff()
    {
        var responsesApi = new FakeResponsesApi(
            JsonDocument.Parse("""
                {
                  "id": "resp_1",
                  "output": [
                    { "type": "function_call", "call_id": "call_1", "name": "resolveTicker", "arguments": "{}" },
                    { "type": "function_call", "call_id": "call_2", "name": "handoffToSpecialist", "arguments": "{}" }
                  ]
                }
                """));
        var toolRegistry = new StubToolRegistry();
        var service = new ResponsesCoordinatorAgentService(
            responsesApi,
            new StaticCoordinatorPromptProvider("Use tools only."),
            toolRegistry,
            Options.Create(new OpenAiResponsesOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                CoordinatorModel = "gpt-5.4-nano"
            }));

        var session = new AutonomousJobSession(
            Guid.NewGuid(),
            "AAPL",
            "corr",
            ResearchExecutionMode.Mock,
            new ResolvedTickerInfo("AAPL", "Apple Inc.", "NASDAQ", "0000320193"));

        var handoff = await service.ExecuteAsync(session, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal("AAPL", handoff.Identifier);
        var tools = Assert.IsType<JsonArray>(responsesApi.Bodies.Single()["tools"]);
        var toolNames = tools.Select(item => item?["name"]?.GetValue<string>()).OfType<string>().ToArray();
        Assert.Equal(
            ["resolveTicker", "getCompanyProfile", "getMarketSnapshot", "getFinancialStatements", "getNewsCoverage", "getFilingCoverage", "getEvidenceStatus", "handoffToSpecialist"],
            toolNames);
    }

    private sealed class StaticCoordinatorPromptProvider : ICoordinatorAgentPromptProvider
    {
        private readonly string _prompt;

        public StaticCoordinatorPromptProvider(string prompt)
        {
            _prompt = prompt;
        }

        public string GetPrompt() => _prompt;
    }

    private sealed class StubToolRegistry : IResearchToolRegistry
    {
        public Task<string> ExecuteAsync(AutonomousJobSession session, string toolName, string arguments, Func<AutonomousProgressEvent, Task> reportProgressAsync, CancellationToken cancellationToken)
        {
            if (toolName == "handoffToSpecialist")
            {
                session.HandoffOccurred = true;
                session.HandoffPacket = new HandoffPacket(
                    session.Identifier,
                    session.ResolvedTicker,
                    TestEvidenceFactory.Create("AAPL"),
                    Array.Empty<string>(),
                    Array.Empty<ProviderUsageResponse>(),
                    "plan",
                    Array.Empty<string>(),
                    Array.Empty<string>());
            }

            return Task.FromResult("{}");
        }
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

        public Task<JsonDocument> CreateResponseStreamAsync(JsonObject body, int timeoutSeconds, Func<ReportChatProviderEvent, Task>? onStreamEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static JsonObject Clone(JsonObject body) => JsonNode.Parse(body.ToJsonString())!.AsObject();
    }
}
