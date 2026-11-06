using Proofsheet.Application.Abstractions;
using Proofsheet.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Proofsheet.Infrastructure.Chat;

public sealed class OpenAiResponsesReportChatAvailabilityService : IReportChatAvailabilityService
{
    private readonly OpenAiResponsesOptions _options;

    public OpenAiResponsesReportChatAvailabilityService(IOptions<OpenAiResponsesOptions> options)
    {
        _options = options.Value;
    }

    public bool IsConfigured =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.ApiKey)
        && !string.IsNullOrWhiteSpace(string.IsNullOrWhiteSpace(_options.ChatModel) ? _options.Model : _options.ChatModel);

    public string UnavailableReason =>
        "Report chat is not configured. Set OpenAiResponses:Enabled, OpenAiResponses:ApiKey, and OpenAiResponses:ChatModel or OpenAiResponses:Model.";
}
