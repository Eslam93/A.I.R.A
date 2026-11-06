using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Proofsheet.Application.Services;

public sealed class ResearchJobSubmissionService : IResearchJobSubmissionService
{
    private readonly IIdentifierValidator _identifierValidator;
    private readonly ITickerDirectoryService _tickerDirectoryService;
    private readonly IResearchExecutionModeResolver _executionModeResolver;
    private readonly IResearchJobStore _jobStore;
    private readonly IResearchJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ResearchExecutionOptions _options;
    private readonly ILogger<ResearchJobSubmissionService> _logger;

    public ResearchJobSubmissionService(
        IIdentifierValidator identifierValidator,
        ITickerDirectoryService tickerDirectoryService,
        IResearchExecutionModeResolver executionModeResolver,
        IResearchJobStore jobStore,
        IResearchJobQueue jobQueue,
        TimeProvider timeProvider,
        IOptions<ResearchExecutionOptions> options,
        ILogger<ResearchJobSubmissionService> logger)
    {
        _identifierValidator = identifierValidator;
        _tickerDirectoryService = tickerDirectoryService;
        _executionModeResolver = executionModeResolver;
        _jobStore = jobStore;
        _jobQueue = jobQueue;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public ResearchJobSubmissionService(
        IIdentifierValidator identifierValidator,
        ITickerDirectoryService tickerDirectoryService,
        IResearchExecutionModeResolver executionModeResolver,
        IResearchJobStore jobStore,
        IResearchJobQueue jobQueue,
        TimeProvider timeProvider)
        : this(
            identifierValidator,
            tickerDirectoryService,
            executionModeResolver,
            jobStore,
            jobQueue,
            timeProvider,
            Options.Create(new ResearchExecutionOptions()),
            NullLogger<ResearchJobSubmissionService>.Instance)
    {
    }

    public async Task<ResearchJobSubmissionResult> SubmitAsync(
        string? identifier,
        ResearchMode? researchMode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = _identifierValidator.Validate(identifier);
        if (!validation.IsValid)
        {
            return ResearchJobSubmissionResult.Rejected(validation.Problem!);
        }

        var normalizedIdentifier = validation.NormalizedIdentifier!;
        var resolvedTicker = await _tickerDirectoryService.ResolveAsync(normalizedIdentifier, cancellationToken);
        if (resolvedTicker is null)
        {
            return ResearchJobSubmissionResult.Rejected(
                new ProblemDescriptor(
                    422,
                    "/problems/unsupported-identifier",
                    "Unsupported identifier.",
                    "The identifier is syntactically valid but could not be resolved as a supported US-listed operating company.",
                    "UnsupportedIdentifier",
                    Identifier: normalizedIdentifier));
        }

        var executionMode = _executionModeResolver.Resolve();
        var effectiveResearchMode = researchMode ?? ResearchMode.Standard;
        ResearchJobRecord record;
        do
        {
            record = new ResearchJobRecord(
                Guid.NewGuid(),
                normalizedIdentifier,
                JobStatus.Queued,
                _timeProvider.GetUtcNow(),
                null,
                null,
                JobStatus.Queued.ToString(),
                Array.Empty<string>(),
                null,
                Guid.NewGuid().ToString("N"),
                Array.Empty<ResearchTraceEvent>(),
                null,
                effectiveResearchMode,
                AgentRole.None,
                string.Empty);
        }
        while (!_jobStore.TryAdd(record));

        if (!_jobQueue.TryEnqueue(new ResearchJobQueueItem(record.JobId, record.Identifier, record.CorrelationId, effectiveResearchMode, executionMode, resolvedTicker)))
        {
            _jobStore.TryRemove(record.JobId);
            _logger.LogWarning(
                "Queue admission failed for {JobId} {Identifier} {CorrelationId}",
                record.JobId,
                record.Identifier,
                record.CorrelationId);

            return ResearchJobSubmissionResult.Rejected(
                new ProblemDescriptor(
                    503,
                    "/problems/queue-capacity-exceeded",
                    "Queue capacity exceeded.",
                    "The service is temporarily unable to accept additional research jobs.",
                    "QueueCapacityExceeded",
                    Identifier: record.Identifier,
                    CorrelationId: record.CorrelationId));
        }

        _logger.LogInformation(
            "Accepted research job {JobId} for {Identifier} with correlation {CorrelationId} in {ResearchMode} mode",
            record.JobId,
            record.Identifier,
            record.CorrelationId,
            effectiveResearchMode);

        return ResearchJobSubmissionResult.Accepted(
            new ResearchJobAcceptedResponse(
                record.JobId,
                JobStatus.Queued,
                record.CreatedAtUtc,
                $"/api/v1/research-jobs/{record.JobId}",
                $"/api/v1/research-jobs/{record.JobId}/result"));
    }
}
