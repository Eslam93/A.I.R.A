using System.Text.Json;
using System.Text.Json.Nodes;
using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Infrastructure.Options;
using Aira.Infrastructure.Reasoning;
using Microsoft.Extensions.Options;

namespace Aira.Infrastructure.Autonomous;

public sealed class ResponsesCoordinatorAgentService : ICoordinatorAgentService
{
    private readonly IResponsesApi _responsesApi;
    private readonly ICoordinatorAgentPromptProvider _promptProvider;
    private readonly IResearchToolRegistry _toolRegistry;
    private readonly OpenAiResponsesOptions _options;

    public ResponsesCoordinatorAgentService(
        IResponsesApi responsesApi,
        ICoordinatorAgentPromptProvider promptProvider,
        IResearchToolRegistry toolRegistry,
        IOptions<OpenAiResponsesOptions> options)
    {
        _responsesApi = responsesApi;
        _promptProvider = promptProvider;
        _toolRegistry = toolRegistry;
        _options = options.Value;
    }

    public async Task<HandoffPacket> ExecuteAsync(
        AutonomousJobSession session,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var model = ResolveCoordinatorModel();
        session.CoordinatorModel = model;
        var instructions = BuildInstructions(session);
        var previousResponseId = string.Empty;
        JsonNode currentInput = JsonValue.Create(
            $"Run the autonomous research workflow for {session.ResolvedTicker.CompanyName} ({session.Identifier}). Use the approved tools, review evidence gaps, and hand off to the specialist when ready.")!;

        for (var round = 0; round < Math.Max(1, _options.CoordinatorMaxToolRounds); round++)
        {
            using var response = await _responsesApi.CreateResponseAsync(
                BuildRequestBody(model, instructions, currentInput, previousResponseId),
                _options.TimeoutSeconds,
                cancellationToken);

            var responseId = response.RootElement.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(responseId))
            {
                throw new InvalidOperationException("Coordinator response did not include an id.");
            }

            var functionCalls = ExtractFunctionCalls(response.RootElement);
            if (functionCalls.Count == 0)
            {
                var assistantText = StructuredReasoningMaterializer.ExtractAssistantText(response.RootElement);
                if (!string.IsNullOrWhiteSpace(assistantText) && session.Plan is null)
                {
                    session.Plan = new CoordinatorPlan(
                        ["resolveTicker", "getCompanyProfile", "getMarketSnapshot", "getFinancialStatements", "getNewsCoverage", "getFilingCoverage", "getEvidenceStatus", "handoffToSpecialist"],
                        assistantText.Trim());

                    await reportProgressAsync(
                        new AutonomousProgressEvent(
                            Domain.Enums.WorkflowStep.CoordinatorPlanning,
                            Domain.Enums.AgentRole.Coordinator,
                            "Planning",
                            Domain.Enums.TraceStatus.Completed,
                            "Coordinator produced an initial dynamic plan and is moving into tool execution."));
                }

                previousResponseId = responseId;
                currentInput = JsonValue.Create("Continue using only the approved tools. Do not answer in prose. Call handoffToSpecialist when the evidence package is ready.")!;
                continue;
            }

            previousResponseId = responseId;
            var toolOutputs = new JsonArray();
            foreach (var call in functionCalls)
            {
                var output = await _toolRegistry.ExecuteAsync(session, call.Name, call.Arguments, reportProgressAsync, cancellationToken);
                toolOutputs.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = call.CallId,
                    ["output"] = output
                });
            }

            if (session.HandoffPacket is not null)
            {
                return session.HandoffPacket;
            }

            currentInput = toolOutputs;
        }

        throw new InvalidOperationException("Coordinator exceeded the maximum tool rounds before specialist handoff.");
    }

    private void EnsureConfigured()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(ResolveCoordinatorModel()))
        {
            throw new InvalidOperationException("Autonomous coordinator is not configured.");
        }
    }

    private JsonObject BuildRequestBody(string model, string instructions, JsonNode input, string previousResponseId)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["instructions"] = instructions,
            ["input"] = input,
            ["tools"] = BuildTools()
        };

        if (!string.IsNullOrWhiteSpace(previousResponseId))
        {
            body["previous_response_id"] = previousResponseId;
        }

        return body;
    }

    private string BuildInstructions(AutonomousJobSession session)
    {
        return
            $"{_promptProvider.GetPrompt().Trim()}\n\n" +
            $"Job context:\n" +
            $"- Identifier: {session.Identifier}\n" +
            $"- Company: {session.ResolvedTicker.CompanyName}\n" +
            $"- Exchange: {session.ResolvedTicker.Exchange}\n" +
            $"- Execution mode: {session.ExecutionMode}\n" +
            $"- Critical domains: CompanyProfile, FinancialStatement\n" +
            $"- Unstructured minimum: NewsCoverage or FilingCoverage\n" +
            $"- You must attempt all retrieval domains before handoff unless a critical failure makes the job impossible.\n" +
            $"- Use only the approved tools and end by calling handoffToSpecialist when ready.";
    }

    private static JsonArray BuildTools()
    {
        return new JsonArray
        {
            BuildTool("resolveTicker", "Resolve and confirm the ticker and company identity for this job."),
            BuildTool("getCompanyProfile", "Collect company profile facts for this job."),
            BuildTool("getMarketSnapshot", "Collect the market snapshot for this job."),
            BuildTool("getFinancialStatements", "Collect financial statements for this job."),
            BuildTool("getNewsCoverage", "Collect news coverage for this job."),
            BuildTool("getFilingCoverage", "Collect filing coverage for this job."),
            BuildTool("getEvidenceStatus", "Review evidence quality, attempts, contradictions, and readiness for handoff."),
            BuildTool("handoffToSpecialist", "Hand off to the stock report specialist when the evidence package is ready.")
        };
    }

    private static JsonObject BuildTool(string name, string description)
    {
        return new JsonObject
        {
            ["type"] = "function",
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            }
        };
    }

    private string ResolveCoordinatorModel() =>
        string.IsNullOrWhiteSpace(_options.CoordinatorModel)
            ? _options.Model
            : _options.CoordinatorModel;

    private static List<OpenAiFunctionCall> ExtractFunctionCalls(JsonElement response)
    {
        var calls = new List<OpenAiFunctionCall>();
        if (!response.TryGetProperty("output", out var outputElement) || outputElement.ValueKind != JsonValueKind.Array)
        {
            return calls;
        }

        foreach (var item in outputElement.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeElement)
                || !string.Equals(typeElement.GetString(), "function_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var callId = item.TryGetProperty("call_id", out var callIdElement)
                ? callIdElement.GetString()
                : item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var name = item.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var arguments = item.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.GetString() ?? "{}"
                : "{}";

            if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            calls.Add(new OpenAiFunctionCall(callId, name, arguments));
        }

        return calls;
    }

    private sealed record OpenAiFunctionCall(string CallId, string Name, string Arguments);
}
