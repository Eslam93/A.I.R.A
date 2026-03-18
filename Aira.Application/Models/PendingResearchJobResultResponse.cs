using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed record PendingResearchJobResultResponse(
    Guid JobId,
    JobStatus Status,
    string Message);
