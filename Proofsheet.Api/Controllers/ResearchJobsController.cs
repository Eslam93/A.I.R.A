using Proofsheet.Api.Models;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Proofsheet.Api.Controllers;

[ApiController]
[Route("api/v1/research-jobs")]
public sealed class ResearchJobsController : ControllerBase
{
    private readonly IResearchJobSubmissionService _submissionService;
    private readonly IResearchJobQueryService _queryService;
    private readonly ResearchExecutionOptions _options;

    public ResearchJobsController(
        IResearchJobSubmissionService submissionService,
        IResearchJobQueryService queryService,
        IOptions<ResearchExecutionOptions> options)
    {
        _submissionService = submissionService;
        _queryService = queryService;
        _options = options.Value;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ResearchJobAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SubmitAsync([FromBody] SubmitResearchJobRequest? request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(value => value.Errors).FirstOrDefault()?.ErrorMessage;
            return this.ToProblemResult(
                new ProblemDescriptor(
                    400,
                    "/problems/invalid-request",
                    "Invalid request.",
                    string.IsNullOrWhiteSpace(firstError) ? "The request body is invalid." : firstError,
                    "InvalidRequest"));
        }

        var result = await _submissionService.SubmitAsync(request?.Identifier, request?.ResearchMode, cancellationToken);
        if (result.IsAccepted)
        {
            return Accepted(result.AcceptedResponse!.StatusUrl, result.AcceptedResponse);
        }

        if (result.Problem!.ErrorCode == "QueueCapacityExceeded")
        {
            Response.Headers.RetryAfter = _options.OverloadRetryAfterSeconds.ToString();
        }

        return this.ToProblemResult(result.Problem);
    }

    [HttpGet("{jobId:guid}")]
    [ProducesResponseType(typeof(ResearchJobStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult GetStatus(Guid jobId)
    {
        var record = _queryService.Get(jobId);
        if (record is null)
        {
            return this.ToProblemResult(
                new ProblemDescriptor(
                    404,
                    "/problems/job-not-found",
                    "Job not found.",
                    "The specified research job does not exist.",
                    "JobNotFound",
                    JobId: jobId));
        }

        return Ok(
            new ResearchJobStatusResponse(
                record.JobId,
                record.Identifier,
                record.Status,
                record.CreatedAtUtc,
                record.StartedAtUtc,
                record.CompletedAtUtc,
                record.CurrentStep,
                record.Warnings,
                record.Trace,
                record.Error,
                record.CorrelationId,
                record.ResearchMode,
                record.ActiveAgent,
                record.AgentPhase));
    }

    [HttpGet("{jobId:guid}/result")]
    [ProducesResponseType(typeof(PendingResearchJobResultResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(CompletedResearchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public IActionResult GetResult(Guid jobId)
    {
        var record = _queryService.Get(jobId);
        if (record is null)
        {
            return this.ToProblemResult(
                new ProblemDescriptor(
                    404,
                    "/problems/job-not-found",
                    "Job not found.",
                    "The specified research job does not exist.",
                    "JobNotFound",
                    JobId: jobId));
        }

        return record.Status switch
        {
            JobStatus.Completed => Ok(record.Result),
            JobStatus.Failed => this.ToProblemResult(
                new ProblemDescriptor(
                    409,
                    "/problems/job-failed",
                    "Job failed.",
                    record.Error?.Message ?? "The research job failed.",
                    "JobFailed",
                    record.JobId,
                    record.Identifier,
                    record.CorrelationId)),
            _ => Accepted(
                new PendingResearchJobResultResponse(
                    record.JobId,
                    record.Status,
                    "Result is not available yet."))
        };
    }
}
