using Aira.Application.Abstractions;
using Aira.Application.Models;

namespace Aira.Application.Services;

public sealed class JobExecutionContextAccessor : IJobExecutionContextAccessor
{
    public JobExecutionContext? Current { get; set; }
}
