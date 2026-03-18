using Aira.Application.Models;
using Aira.Domain.Models;

namespace Aira.Application.Abstractions;

public interface IStockReportSpecialistService
{
    Task<ReasoningResult> GenerateAsync(
        AutonomousJobSession session,
        HandoffPacket handoffPacket,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken);
}
