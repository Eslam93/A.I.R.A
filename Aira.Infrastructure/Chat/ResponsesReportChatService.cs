using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Aira.Infrastructure.Chat;

public sealed class ResponsesReportChatService : IReportChatService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IResponsesApi _responsesApi;
    private readonly IReportChatPromptProvider _promptProvider;
    private readonly OpenAiResponsesOptions _options;

    public ResponsesReportChatService(
        IResponsesApi responsesApi,
        IReportChatPromptProvider promptProvider,
        IOptions<OpenAiResponsesOptions> options)
    {
        _responsesApi = responsesApi;
        _promptProvider = promptProvider;
        _options = options.Value;
    }

    public async Task<ReportChatExecutionResult> SendMessageAsync(
        CompletedResearchResult reportSnapshot,
        string userMessage,
        string previousResponseId,
        Func<ReportChatProviderEvent, Task>? onProviderEvent = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new InvalidOperationException("User message is required.");
        }

        EnsureConfigured();

        var instructions = BuildInstructions(_promptProvider.GetPrompt(), reportSnapshot);
        var currentPreviousResponseId = previousResponseId ?? string.Empty;
        JsonNode currentInput = JsonValue.Create(userMessage.Trim())!;

        for (var round = 0; round <= _options.MaxToolRounds; round++)
        {
            using var response = _options.EnableStreaming && onProviderEvent is not null
                ? await _responsesApi.CreateResponseStreamAsync(
                    BuildRequestBody(instructions, currentInput, currentPreviousResponseId),
                    _options.TimeoutSeconds,
                    onProviderEvent,
                    cancellationToken)
                : await _responsesApi.CreateResponseAsync(
                    BuildRequestBody(instructions, currentInput, currentPreviousResponseId),
                    _options.TimeoutSeconds,
                    cancellationToken);

            var responseId = response.RootElement.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(responseId))
            {
                throw new InvalidOperationException("Responses API response is missing id.");
            }

            var functionCalls = ExtractFunctionCalls(response.RootElement);
            if (functionCalls.Count == 0)
            {
                var messages = ExtractAssistantMessages(response.RootElement);
                if (messages.Count == 0)
                {
                    throw new InvalidOperationException("Responses output was empty.");
                }

                var answer = string.Join(Environment.NewLine + Environment.NewLine, messages).Trim();
                return new ReportChatExecutionResult(
                    responseId,
                    answer,
                    ExtractCitedSourceIds(answer, reportSnapshot.Sources));
            }

            if (round >= _options.MaxToolRounds)
            {
                throw new InvalidOperationException("Responses tool execution exceeded max rounds.");
            }

            currentPreviousResponseId = responseId;
            currentInput = BuildToolOutputs(reportSnapshot, functionCalls);
        }

        throw new InvalidOperationException("Responses tool execution exceeded max rounds.");
    }

    private void EnsureConfigured()
    {
        if (!_options.Enabled
            || string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(ResolveChatModel()))
        {
            throw new InvalidOperationException("Report chat is not configured.");
        }
    }

    private JsonObject BuildRequestBody(string instructions, JsonNode input, string previousResponseId)
    {
        var body = new JsonObject
        {
            ["model"] = ResolveChatModel(),
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

    private JsonArray BuildTools()
    {
        return new JsonArray
        {
            BuildFunctionTool(
                "getReportSummary",
                "Get a concise summary of the completed report tied to this chat thread.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                }),
            BuildFunctionTool(
                "getSourceDetails",
                "Get detailed information for specific source ids from the completed report.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["sourceIds"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["type"] = "string" }
                        }
                    },
                    ["required"] = new JsonArray("sourceIds")
                }),
            BuildFunctionTool(
                "getInsightEvidence",
                "Get supporting evidence for one or more numbered insights from the completed report.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["insightIndexes"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["type"] = "integer" }
                        }
                    },
                    ["required"] = new JsonArray("insightIndexes")
                }),
            BuildFunctionTool(
                "getTrace",
                "Get trace entries from the completed report, optionally filtered by workflow step and limited in count.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["step"] = new JsonObject { ["type"] = "string" },
                        ["limit"] = new JsonObject { ["type"] = "integer" }
                    }
                }),
            BuildFunctionTool(
                "getProviderUsage",
                "Get provider usage and fallback details for the completed report.",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                })
        };
    }

    private static JsonObject BuildFunctionTool(string name, string description, JsonObject parameters)
    {
        return new JsonObject
        {
            ["type"] = "function",
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = parameters
        };
    }

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

            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
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

    private static List<string> ExtractAssistantMessages(JsonElement response)
    {
        var messages = new List<string>();

        if (response.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in outputElement.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var typeElement))
                {
                    continue;
                }

                var type = typeElement.GetString() ?? string.Empty;
                if (string.Equals(type, "message", StringComparison.OrdinalIgnoreCase))
                {
                    if (!item.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var contentItem in contentElement.EnumerateArray())
                    {
                        if (!contentItem.TryGetProperty("type", out var contentTypeElement))
                        {
                            continue;
                        }

                        var contentType = contentTypeElement.GetString() ?? string.Empty;
                        if (!string.Equals(contentType, "output_text", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(contentType, "text", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (contentItem.TryGetProperty("text", out var textElement))
                        {
                            var text = textElement.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                messages.Add(text);
                            }
                        }
                    }
                }
                else if (string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase)
                    && item.TryGetProperty("text", out var outputText))
                {
                    var text = outputText.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        messages.Add(text);
                    }
                }
            }
        }

        if (messages.Count == 0
            && response.TryGetProperty("output_text", out var aggregateOutput)
            && aggregateOutput.ValueKind == JsonValueKind.String)
        {
            var text = aggregateOutput.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                messages.Add(text);
            }
        }

        return messages;
    }

    private JsonArray BuildToolOutputs(CompletedResearchResult reportSnapshot, IReadOnlyList<OpenAiFunctionCall> functionCalls)
    {
        var outputs = new JsonArray();
        foreach (var functionCall in functionCalls)
        {
            var outputJson = ExecuteTool(reportSnapshot, functionCall);
            outputs.Add(new JsonObject
            {
                ["type"] = "function_call_output",
                ["call_id"] = functionCall.CallId,
                ["output"] = outputJson
            });
        }

        return outputs;
    }

    private string ExecuteTool(CompletedResearchResult reportSnapshot, OpenAiFunctionCall functionCall)
    {
        return functionCall.Name switch
        {
            "getReportSummary" => JsonSerializer.Serialize(BuildReportSummary(reportSnapshot), SerializerOptions),
            "getSourceDetails" => JsonSerializer.Serialize(GetSourceDetails(reportSnapshot, functionCall.Arguments), SerializerOptions),
            "getInsightEvidence" => JsonSerializer.Serialize(GetInsightEvidence(reportSnapshot, functionCall.Arguments), SerializerOptions),
            "getTrace" => JsonSerializer.Serialize(GetTrace(reportSnapshot, functionCall.Arguments), SerializerOptions),
            "getProviderUsage" => JsonSerializer.Serialize(new { items = reportSnapshot.ProviderUsage }, SerializerOptions),
            _ => JsonSerializer.Serialize(new { error = "unsupported_function" }, SerializerOptions)
        };
    }

    private object BuildReportSummary(CompletedResearchResult reportSnapshot)
    {
        return new
        {
            reportSnapshot.Company,
            reportSnapshot.Identifier,
            Signal = reportSnapshot.Signal.ToString(),
            reportSnapshot.Confidence,
            reportSnapshot.Thesis,
            reportSnapshot.SignalDetails,
            reportSnapshot.ConfidenceDetails,
            reportSnapshot.SynthesisDetails,
            reportSnapshot.OrchestrationDetails,
            reportSnapshot.Warnings,
            Insights = reportSnapshot.Insights.Select((insight, index) => new
            {
                index = index + 1,
                text = insight
            }),
            reportSnapshot.Sources
        };
    }

    private object GetSourceDetails(CompletedResearchResult reportSnapshot, string arguments)
    {
        var parsed = JsonSerializer.Deserialize<SourceDetailsArguments>(arguments, SerializerOptions)
            ?? new SourceDetailsArguments(Array.Empty<string>());
        var requested = parsed.SourceIds ?? Array.Empty<string>();
        var matches = reportSnapshot.SourceDetails
            .Where(detail => requested.Contains(detail.SourceId, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var missing = requested.Where(sourceId => matches.All(detail => !string.Equals(detail.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))).ToArray();
        return new
        {
            items = matches,
            missingSourceIds = missing
        };
    }

    private object GetInsightEvidence(CompletedResearchResult reportSnapshot, string arguments)
    {
        var parsed = JsonSerializer.Deserialize<InsightEvidenceArguments>(arguments, SerializerOptions)
            ?? new InsightEvidenceArguments(Array.Empty<int>());
        var requestedIndexes = parsed.InsightIndexes ?? Array.Empty<int>();
        var items = new List<object>();
        var missing = new List<int>();

        foreach (var requestedIndex in requestedIndexes.Distinct())
        {
            var zeroBased = requestedIndex - 1;
            if (zeroBased < 0 || zeroBased >= reportSnapshot.Insights.Count)
            {
                missing.Add(requestedIndex);
                continue;
            }

            var insight = reportSnapshot.Insights[zeroBased];
            var detail = reportSnapshot.InsightDetails.ElementAtOrDefault(zeroBased);
            var sourceDetails = detail?.SourceIds
                .Select(sourceId => reportSnapshot.SourceDetails.FirstOrDefault(x => string.Equals(x.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)))
                .Where(source => source is not null)
                .ToArray();

            items.Add(new
            {
                insightIndex = requestedIndex,
                insight,
                sourceIds = detail?.SourceIds ?? Array.Empty<string>(),
                sourceDetails
            });
        }

        return new
        {
            items,
            missingInsightIndexes = missing.ToArray()
        };
    }

    private object GetTrace(CompletedResearchResult reportSnapshot, string arguments)
    {
        var parsed = JsonSerializer.Deserialize<TraceArguments>(arguments, SerializerOptions)
            ?? new TraceArguments(null, null);
        IEnumerable<ResearchTraceEvent> query = reportSnapshot.Trace;
        if (!string.IsNullOrWhiteSpace(parsed.Step))
        {
            query = query.Where(trace => string.Equals(trace.Step.ToString(), parsed.Step, StringComparison.OrdinalIgnoreCase));
        }

        if (parsed.Limit is > 0)
        {
            query = query.Take(parsed.Limit.Value);
        }

        return new
        {
            items = query.ToArray()
        };
    }

    private static IReadOnlyList<string> ExtractCitedSourceIds(string answer, IReadOnlyList<string> reportSources)
    {
        var cited = new List<string>();
        foreach (var sourceId in reportSources)
        {
            if (answer.Contains(sourceId, StringComparison.OrdinalIgnoreCase))
            {
                cited.Add(sourceId);
            }
        }

        return cited;
    }

    private static string BuildInstructions(string prompt, CompletedResearchResult reportSnapshot)
    {
        var summary = new StringBuilder();
        summary.AppendLine(prompt.Trim());
        summary.AppendLine();
        summary.AppendLine("Report Summary:");
        summary.AppendLine($"Company: {reportSnapshot.Company}");
        summary.AppendLine($"Identifier: {reportSnapshot.Identifier}");
        summary.AppendLine($"Signal: {reportSnapshot.Signal}");
        summary.AppendLine($"Confidence: {reportSnapshot.Confidence}");
        summary.AppendLine($"Thesis: {reportSnapshot.Thesis}");
        if (reportSnapshot.SignalDetails is not null)
        {
            summary.AppendLine($"Signal rationale: {reportSnapshot.SignalDetails.Rationale}");
        }

        if (reportSnapshot.ConfidenceDetails is not null)
        {
            summary.AppendLine($"Confidence rationale: {reportSnapshot.ConfidenceDetails.Rationale}");
        }

        summary.AppendLine($"Warnings: {(reportSnapshot.Warnings.Count == 0 ? "None" : string.Join("; ", reportSnapshot.Warnings))}");
        summary.AppendLine("Insights:");
        for (var index = 0; index < reportSnapshot.Insights.Count; index++)
        {
            summary.AppendLine($"{index + 1}. {reportSnapshot.Insights[index]}");
        }

        summary.AppendLine($"Cited source ids available in the report: {string.Join(", ", reportSnapshot.Sources)}");
        summary.AppendLine("Use tools when you need source details, trace details, provider usage, or evidence not fully present in this summary.");
        return summary.ToString().Trim();
    }

    private string ResolveChatModel() =>
        string.IsNullOrWhiteSpace(_options.ChatModel)
            ? _options.Model
            : _options.ChatModel;

    private sealed record OpenAiFunctionCall(string CallId, string Name, string Arguments);

    private sealed record SourceDetailsArguments(IReadOnlyList<string> SourceIds);

    private sealed record InsightEvidenceArguments(IReadOnlyList<int> InsightIndexes);

    private sealed record TraceArguments(string? Step, int? Limit);
}
