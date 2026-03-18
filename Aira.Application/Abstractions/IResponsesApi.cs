using System.Text.Json;
using System.Text.Json.Nodes;
using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IResponsesApi
{
    Task<JsonDocument> CreateResponseAsync(
        JsonObject body,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);

    Task<JsonDocument> CreateResponseStreamAsync(
        JsonObject body,
        int timeoutSeconds,
        Func<ReportChatProviderEvent, Task>? onStreamEvent,
        CancellationToken cancellationToken = default);
}
