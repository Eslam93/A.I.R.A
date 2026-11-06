using System.Text.Json;

namespace Proofsheet.Tests.Integration.Api;

public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task Swagger_Document_Lists_The_Api_And_Documents_Enums_As_Strings()
    {
        await using var factory = new ProofsheetWebApplicationFactory();
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var root = document.RootElement;

        Assert.StartsWith("3.0", root.GetProperty("openapi").GetString());

        var paths = root.GetProperty("paths");
        string[] expectedPaths =
        [
            "/health",
            "/api/v1/research-jobs",
            "/api/v1/research-jobs/{jobId}",
            "/api/v1/research-jobs/{jobId}/result",
            "/api/v1/tickers/search"
        ];
        foreach (var path in expectedPaths)
        {
            Assert.True(paths.TryGetProperty(path, out _), $"The Swagger document is missing {path}.");
        }

        // The API serializes enums as strings (JsonStringEnumConverter), so the document must describe them that way too.
        var jobStatus = root.GetProperty("components").GetProperty("schemas").GetProperty("JobStatus");
        Assert.Equal("string", jobStatus.GetProperty("type").GetString());
        string?[] expectedStatuses = ["Queued", "Running", "Completed", "Failed"];
        Assert.Equal(expectedStatuses, jobStatus.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());
    }
}
