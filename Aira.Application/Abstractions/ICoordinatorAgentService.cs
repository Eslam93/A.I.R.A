using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface ICoordinatorAgentService
{
    Task<HandoffPacket> ExecuteAsync(
        AutonomousJobSession session,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken);
}
