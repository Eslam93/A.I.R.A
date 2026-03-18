using Aira.Domain.Enums;

namespace Aira.Api.Models;

public sealed class SubmitResearchJobRequest
{
    public string? Identifier { get; init; }

    public ResearchMode? ResearchMode { get; init; }
}
