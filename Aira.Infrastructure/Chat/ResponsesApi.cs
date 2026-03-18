using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aira.Application.Abstractions;
using Aira.Application.Models;

namespace Aira.Infrastructure.Chat;

public sealed class ResponsesApi : IResponsesApi
{
    private readonly HttpClient _httpClient;

    public ResponsesApi(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<JsonDocument> CreateResponseAsync(
        JsonObject body,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        var content = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Responses API HTTP {(int)response.StatusCode}: {content}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Responses API returned an empty body.");
        }

        return ParseDocument(content, "Responses API");
    }

    public async Task<JsonDocument> CreateResponseStreamAsync(
        JsonObject body,
        int timeoutSeconds,
        Func<ReportChatProviderEvent, Task>? onStreamEvent,
        CancellationToken cancellationToken = default)
    {
        body["stream"] = true;

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            throw new InvalidOperationException($"Responses API HTTP {(int)response.StatusCode}: {errorContent}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        using var reader = new StreamReader(stream);

        JsonDocument? completedResponse = null;
        string? eventName = null;
        var dataLines = new List<string>();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                completedResponse = await HandleSseEventAsync(eventName, dataLines, onStreamEvent);
                if (completedResponse is not null)
                {
                    return completedResponse;
                }

                eventName = null;
                dataLines.Clear();
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                dataLines.Add(line[5..].TrimStart());
            }
        }

        if (dataLines.Count > 0)
        {
            completedResponse = await HandleSseEventAsync(eventName, dataLines, onStreamEvent);
        }

        return completedResponse ?? throw new InvalidOperationException("Responses stream ended before a completion event was received.");
    }

    private static async Task<JsonDocument?> HandleSseEventAsync(
        string? eventName,
        List<string> dataLines,
        Func<ReportChatProviderEvent, Task>? onStreamEvent)
    {
        if (dataLines.Count == 0)
        {
            return null;
        }

        var payload = string.Join("\n", dataLines).Trim();
        if (string.IsNullOrWhiteSpace(payload) || string.Equals(payload, "[DONE]", StringComparison.Ordinal))
        {
            return null;
        }

        using var document = ParseDocument(payload, "Responses stream");
        var type = document.RootElement.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString() ?? string.Empty
            : eventName ?? string.Empty;

        if (onStreamEvent is not null)
        {
            await onStreamEvent(BuildProviderEvent(document.RootElement, type, payload));
        }

        if (string.Equals(type, "response.completed", StringComparison.OrdinalIgnoreCase))
        {
            if (document.RootElement.TryGetProperty("response", out var responseElement) && responseElement.ValueKind == JsonValueKind.Object)
            {
                return JsonDocument.Parse(responseElement.GetRawText());
            }

            return JsonDocument.Parse(document.RootElement.GetRawText());
        }

        if (string.Equals(type, "response.failed", StringComparison.OrdinalIgnoreCase))
        {
            var error = document.RootElement.TryGetProperty("error", out var errorElement)
                ? errorElement.GetRawText()
                : "Responses stream failed.";
            throw new InvalidOperationException(error);
        }

        if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(payload);
        }

        return null;
    }

    private static ReportChatProviderEvent BuildProviderEvent(JsonElement payload, string type, string rawJson)
    {
        var responseId = string.Empty;
        if (payload.TryGetProperty("response", out var responseElement)
            && responseElement.ValueKind == JsonValueKind.Object
            && responseElement.TryGetProperty("id", out var nestedId))
        {
            responseId = nestedId.GetString() ?? string.Empty;
        }
        else if (payload.TryGetProperty("response_id", out var responseIdElement))
        {
            responseId = responseIdElement.GetString() ?? string.Empty;
        }
        else if (payload.TryGetProperty("id", out var idElement))
        {
            responseId = idElement.GetString() ?? string.Empty;
        }

        var textDelta = string.Equals(type, "response.output_text.delta", StringComparison.OrdinalIgnoreCase)
            && payload.TryGetProperty("delta", out var deltaElement)
            ? deltaElement.GetString() ?? string.Empty
            : string.Empty;

        return new ReportChatProviderEvent(type, responseId, textDelta, rawJson);
    }

    private static JsonDocument ParseDocument(string content, string source)
    {
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{source} parse failure: {ex.Message}", ex);
        }
    }
}
