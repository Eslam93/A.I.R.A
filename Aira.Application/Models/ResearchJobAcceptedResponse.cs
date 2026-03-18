using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed record ResearchJobAcceptedResponse(
    Guid JobId,
    JobStatus Status,
    DateTimeOffset CreatedAtUtc,
    string StatusUrl,
    string ResultUrl);
