using System.Text.Json;
using System.Text.Json.Nodes;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;
using Proofsheet.Infrastructure.Reasoning;
using Proofsheet.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Proofsheet.Tests.Unit.Processing;

public sealed class OpenAiSelectorAndReasonerTests
{
    [Fact]
    public void Selector_Falls_Back_When_Enabled_But_Misconfigured()
    {
        var selector = new ConfiguredResearchReasonerSelector(
            new DeterministicResearchReasoner(),
            new OpenAiResearchReasoner(new FakeResponsesApi("""{"id":"resp_1","output_text":"{}"}"""), new StubPromptProvider(), Options.Create(new OpenAiResponsesOptions()), new DeterministicResearchReasoner()),
            Options.Create(new OpenAiResponsesOptions { Enabled = true }));

        var selection = selector.Select(ResearchExecutionMode.Hybrid);

        Assert.True(selection.FallbackOccurred);
        Assert.Contains("Deterministic synthesis", selection.WarningMessage);
    }

    [Fact]
    public async Task OpenAiReasoner_Produces_Structured_Synthesis_Using_Responses_Api()
    {
        var responseJson = """
            {
              "id": "resp_1",
              "output_text": "{\"thesis\":\"Rewritten thesis.\",\"signal\":\"Bullish\",\"confidence\":84,\"insights\":[\"Insight A.\",\"Insight B.\",\"Insight C.\"],\"thesisDetails\":{\"summary\":\"Thesis summary.\",\"sourceIds\":[\"financials:AAPL\",\"profile:AAPL\"]},\"insightDetails\":[{\"insight\":\"Insight A.\",\"sourceIds\":[\"financials:AAPL\"]},{\"insight\":\"Insight B.\",\"sourceIds\":[\"news:AAPL:001\"]},{\"insight\":\"Insight C.\",\"sourceIds\":[\"profile:AAPL\",\"filing:AAPL:001\"]}],\"signalDetails\":{\"rationale\":\"Signal is supported by financials and profile context.\",\"sourceIds\":[\"financials:AAPL\",\"profile:AAPL\"]},\"confidenceDetails\":{\"rationale\":\"Confidence is supported by strong evidence coverage.\",\"sourceIds\":[\"financials:AAPL\",\"filing:AAPL:001\"]}}"
            }
            """;
        var reasoner = new OpenAiResearchReasoner(
            new FakeResponsesApi(responseJson),
            new StubPromptProvider(),
            Options.Create(new OpenAiResponsesOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                ReportModel = "test-model"
            }),
            new DeterministicResearchReasoner());

        var result = await reasoner.SynthesizeAsync(TestEvidenceFactory.Create("AAPL"), CancellationToken.None);

        Assert.Equal("Rewritten thesis.", result.Thesis);
        Assert.Equal(["Insight A.", "Insight B.", "Insight C."], result.Insights);
        Assert.Equal(InvestmentSignal.Bullish, result.Signal);
        Assert.Equal(84, result.Confidence);
        Assert.Equal("Signal is supported by financials and profile context.", result.SignalDetails.Rationale);
    }

    private sealed class FakeResponsesApi : IResponsesApi
    {
        private readonly string _json;

        public FakeResponsesApi(string json)
        {
            _json = json;
        }

        public Task<JsonDocument> CreateResponseAsync(JsonObject body, int timeoutSeconds, CancellationToken cancellationToken = default) =>
            Task.FromResult(JsonDocument.Parse(_json));

        public Task<JsonDocument> CreateResponseStreamAsync(JsonObject body, int timeoutSeconds, Func<ReportChatProviderEvent, Task>? onStreamEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPromptProvider : IReportSynthesisPromptProvider
    {
        public string GetPrompt() => "Return JSON only.";
    }
}
