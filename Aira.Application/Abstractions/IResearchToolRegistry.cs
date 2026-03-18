using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IResearchToolRegistry
{
    Task<string> ExecuteAsync(
        AutonomousJobSession session,
        string toolName,
        string arguments,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken);
}
