using Aira.Application.Abstractions;
using Aira.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aira.Infrastructure.Chat;

public sealed class FileSpecialistAgentPromptProvider : ISpecialistAgentPromptProvider
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly OpenAiResponsesOptions _options;
    private string? _cachedPrompt;

    public FileSpecialistAgentPromptProvider(
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

        var configuredPath = !string.IsNullOrWhiteSpace(_options.SpecialistPromptFilePath)
            ? _options.SpecialistPromptFilePath
            : "Prompts/autonomous-specialist-system-prompt.txt";
        var absolutePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_hostEnvironment.ContentRootPath, configuredPath);

        if (!File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"Specialist prompt file was not found: {absolutePath}");
        }

        _cachedPrompt = File.ReadAllText(absolutePath).Trim();
        if (string.IsNullOrWhiteSpace(_cachedPrompt))
        {
            throw new InvalidOperationException("Specialist prompt file is empty.");
        }

        return _cachedPrompt;
    }
}
