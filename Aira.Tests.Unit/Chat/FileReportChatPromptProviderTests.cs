using Aira.Infrastructure.Chat;
using Aira.Infrastructure.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aira.Tests.Unit.Chat;

public sealed class FileReportChatPromptProviderTests
{
    [Fact]
    public void GetPrompt_Loads_Trimmed_Prompt_From_Configured_File()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aira-prompt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var relativePath = Path.Combine("Prompts", "report-chat-system-prompt.txt");
            var absolutePath = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, "  Prompt body for report chat.  ");

            var provider = new FileReportChatPromptProvider(
                new TestHostEnvironment(root),
                Options.Create(new OpenAiResponsesOptions { PromptFilePath = relativePath }));

            var prompt = provider.GetPrompt();

            Assert.Equal("Prompt body for report chat.", prompt);
            Assert.Equal(prompt, provider.GetPrompt());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
        }

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Aira.Tests.Unit";

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
