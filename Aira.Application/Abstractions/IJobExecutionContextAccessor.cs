using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IJobExecutionContextAccessor
{
    JobExecutionContext? Current { get; set; }
}
