using Proofsheet.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Proofsheet.Api;

public static class ProblemDetailsFactoryExtensions
{
    public static ProblemDetails BuildProblemDetails(HttpContext context, ProblemDescriptor descriptor)
    {
        var problem = new ProblemDetails
        {
            Type = descriptor.Type,
            Title = descriptor.Title,
            Status = descriptor.StatusCode,
            Detail = descriptor.Detail,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;
        problem.Extensions["errorCode"] = descriptor.ErrorCode;

        if (!string.IsNullOrWhiteSpace(descriptor.CorrelationId))
        {
            problem.Extensions["correlationId"] = descriptor.CorrelationId;
        }

        if (descriptor.JobId.HasValue)
        {
            problem.Extensions["jobId"] = descriptor.JobId.Value;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.Identifier))
        {
            problem.Extensions["identifier"] = descriptor.Identifier;
        }

        return problem;
    }

    public static ObjectResult ToProblemResult(this ControllerBase controller, ProblemDescriptor descriptor)
    {
        var problem = BuildProblemDetails(controller.HttpContext, descriptor);
        return new ObjectResult(problem)
        {
            StatusCode = descriptor.StatusCode
        };
    }
}
