using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;

namespace Proofsheet.Application.Services;

public sealed class JobExecutionContextAccessor : IJobExecutionContextAccessor
{
    public JobExecutionContext? Current { get; set; }
}
