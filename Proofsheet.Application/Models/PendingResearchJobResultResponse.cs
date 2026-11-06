using Proofsheet.Domain.Enums;

namespace Proofsheet.Application.Models;

public sealed record PendingResearchJobResultResponse(
    Guid JobId,
    JobStatus Status,
    string Message);
