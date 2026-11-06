using Proofsheet.Application.Abstractions;
using Proofsheet.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Proofsheet.Infrastructure.Chat;

public sealed class FileReportChatPromptProvider : IReportChatPromptProvider
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly OpenAiResponsesOptions _options;
    private string? _cachedPrompt;

    public FileReportChatPromptProvider(
        IHostEnvironment hostEnvironment,
        IOptions<OpenAiResponsesOptions> options)
    {
        _hostEnvironment = hostEnvironment;
        _options = options.Value;
    }

    public string GetPrompt()
    {
        if (!string.IsNullOrWhiteSpace(_cachedPrompt))
        {
            return _cachedPrompt;
        }

        var configuredPath = !string.IsNullOrWhiteSpace(_options.ChatPromptFilePath)
            ? _options.ChatPromptFilePath
            : _options.PromptFilePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("OpenAiResponses:ChatPromptFilePath is not configured.");
        }

        var absolutePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_hostEnvironment.ContentRootPath, configuredPath);

        if (!File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Report chat prompt file was not found at '{absolutePath}'.");
        }

        _cachedPrompt = File.ReadAllText(absolutePath).Trim();
        if (string.IsNullOrWhiteSpace(_cachedPrompt))
        {
            throw new InvalidOperationException("Report chat prompt file is empty.");
        }

        return _cachedPrompt;
    }
}
