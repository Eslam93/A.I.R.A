using Proofsheet.Application.Models;
using Proofsheet.Domain.Models;

namespace Proofsheet.Application.Abstractions;

public interface IStockReportSpecialistService
{
    Task<ReasoningResult> GenerateAsync(
        AutonomousJobSession session,
        HandoffPacket handoffPacket,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken);
}
