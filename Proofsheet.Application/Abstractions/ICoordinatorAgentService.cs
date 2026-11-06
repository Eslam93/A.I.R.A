using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface ICoordinatorAgentService
{
    Task<HandoffPacket> ExecuteAsync(
        AutonomousJobSession session,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken);
}
