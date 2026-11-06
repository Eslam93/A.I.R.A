using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IJobExecutionContextAccessor
{
    JobExecutionContext? Current { get; set; }
}
