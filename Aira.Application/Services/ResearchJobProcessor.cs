using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Domain.Contracts;
using Aira.Domain.Enums;
using Aira.Domain.Models;
using Aira.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aira.Application.Services;

public sealed class ResearchJobProcessor : IResearchJobProcessor
{
    private const string JobFailedErrorCode = "JobFailed";

    private readonly IResearchJobStore _jobStore;
    private readonly IJobExecutionContextAccessor _jobExecutionContextAccessor;
    private readonly ICompanyProfileProvider _companyProfileProvider;
    private readonly IMarketSnapshotProvider _marketSnapshotProvider;
    private readonly IFinancialStatementProvider _financialStatementProvider;
    private readonly INewsProvider _newsProvider;
    private readonly IFilingExcerptProvider _filingExcerptProvider;
    private readonly IResearchReasonerSelector _reasonerSelector;
    private readonly IAutonomousResearchOrchestrator _autonomousResearchOrchestrator;
    private readonly TimeProvider _timeProvider;
    private readonly ResearchExecutionOptions _options;
    private readonly ILogger<ResearchJobProcessor> _logger;

    public ResearchJobProcessor(
        IResearchJobStore jobStore,
        IJobExecutionContextAccessor jobExecutionContextAccessor,
        ICompanyProfileProvider companyProfileProvider,
        IMarketSnapshotProvider marketSnapshotProvider,
        IFinancialStatementProvider financialStatementProvider,
        INewsProvider newsProvider,
        IFilingExcerptProvider filingExcerptProvider,
        IResearchReasonerSelector reasonerSelector,
        IAutonomousResearchOrchestrator autonomousResearchOrchestrator,
        TimeProvider timeProvider,
        IOptions<ResearchExecutionOptions> options,
        ILogger<ResearchJobProcessor> logger)
    {
        _jobStore = jobStore;
        _jobExecutionContextAccessor = jobExecutionContextAccessor;
        _companyProfileProvider = companyProfileProvider;
        _marketSnapshotProvider = marketSnapshotProvider;
        _financialStatementProvider = financialStatementProvider;
        _newsProvider = newsProvider;
        _filingExcerptProvider = filingExcerptProvider;
        _reasonerSelector = reasonerSelector;
        _autonomousResearchOrchestrator = autonomousResearchOrchestrator;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(ResearchJobQueueItem item, CancellationToken cancellationToken)
    {
        using var logScope = _logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["jobId"] = item.JobId,
                ["identifier"] = item.Identifier,
                ["correlationId"] = item.CorrelationId
            });

        _logger.LogInformation("Dequeued research job {JobId}", item.JobId);

        var existingRecord = _jobStore.Get(item.JobId);
        if (existingRecord is null)
        {
            _logger.LogError("Job {JobId} was dequeued but not found in the store.", item.JobId);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var effectiveJobTimeout = ResolveEffectiveJobTimeout(item.ExecutionMode);
        timeoutCts.CancelAfter(effectiveJobTimeout);
        var jobToken = timeoutCts.Token;
        var warnings = new List<string>(existingRecord.Warnings);
        var trace = new List<ResearchTraceEvent>(existingRecord.Trace);
        _jobExecutionContextAccessor.Current = new JobExecutionContext
        {
            JobId = item.JobId,
            Identifier = item.Identifier,
            CorrelationId = item.CorrelationId,
            ExecutionMode = item.ExecutionMode,
            ResolvedTicker = item.ResolvedTicker
        };

        _logger.LogInformation(
            "Using effective job timeout of {TimeoutSeconds} seconds for job {JobId} in {ExecutionMode} mode.",
            effectiveJobTimeout.TotalSeconds,
            item.JobId,
            item.ExecutionMode);

        try
        {
            if (item.ResearchMode == ResearchMode.Autonomous)
            {
                UpdateRecord(
                    item.JobId,
                    record => ResearchJobStateMachine.MarkRunning(
                        record,
                        _timeProvider.GetUtcNow(),
                        WorkflowStep.IdentifierResolution.ToString(),
                        AgentRole.Coordinator,
                        "Planning"));

                var autonomousResult = await _autonomousResearchOrchestrator.ExecuteAsync(item, jobToken);
                UpdateRecord(
                    item.JobId,
                    record => ResearchJobStateMachine.MarkCompleted(
                        record,
                        autonomousResult.Result.GeneratedAtUtc,
                        autonomousResult.Result,
                        autonomousResult.Warnings,
                        autonomousResult.Trace));

                _logger.LogInformation("Completed autonomous research job {JobId} with signal {Signal}", item.JobId, autonomousResult.Result.Signal);
                return;
            }

            UpdateRecord(
                item.JobId,
                record => ResearchJobStateMachine.MarkRunning(record, _timeProvider.GetUtcNow(), WorkflowStep.IdentifierResolution.ToString()));
            AddTrace(trace, WorkflowStep.IdentifierResolution, TraceStatus.Completed, $"Identifier {item.Identifier} was normalized and resolved to {item.ResolvedTicker.CompanyName}.");

            UpdateProgress(item.JobId, WorkflowStep.ResearchPlanCreation.ToString(), warnings, trace);
            AddTrace(trace, WorkflowStep.ResearchPlanCreation, TraceStatus.Completed, "Planned collection for company profile, market snapshot, financial statements, news, and filing excerpts.");

            var reasonerSelection = _reasonerSelector.Select(item.ExecutionMode);
            if (!reasonerSelection.IsAvailable || reasonerSelection.Reasoner is null)
            {
                FailJob(item.JobId, warnings, trace, reasonerSelection.FailureMessage ?? "Report synthesis is not available for the selected execution mode.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(reasonerSelection.WarningMessage))
            {
                warnings.Add(reasonerSelection.WarningMessage);
                AddTrace(trace, WorkflowStep.ProviderFallback, TraceStatus.Warning, reasonerSelection.WarningMessage);
            }

            UpdateProgress(item.JobId, WorkflowStep.ParallelSourceCollection.ToString(), warnings, trace);
            var providerResults = await CollectSourcesAsync(item.Identifier, jobToken);

            MergeProviderSideEffects(providerResults, warnings, trace);

            if (providerResults.CompanyProfile.Result.Status != ProviderResultStatus.Success)
            {
                FailCriticalSource(item.JobId, warnings, trace, "Company profile was unavailable after final attempt.");
                return;
            }

            if (providerResults.FinancialStatement.Result.Status != ProviderResultStatus.Success)
            {
                FailCriticalSource(item.JobId, warnings, trace, "Financial statements were unavailable after final attempt.");
                return;
            }

            var missingNonCriticalSourceCount = 0;
            missingNonCriticalSourceCount += HandleNonCriticalAvailability(providerResults.MarketSnapshot, warnings, trace);
            missingNonCriticalSourceCount += HandleNonCriticalAvailability(providerResults.NewsCoverage, warnings, trace);
            missingNonCriticalSourceCount += HandleNonCriticalAvailability(providerResults.FilingCoverage, warnings, trace);

            UpdateProgress(item.JobId, WorkflowStep.QualityAssessment.ToString(), warnings, trace);
            var scoringInput = new ResearchScoringInput(
                providerResults.CompanyProfile.Result.Value,
                providerResults.MarketSnapshot.Result.Value,
                providerResults.FinancialStatement.Result.Value,
                providerResults.NewsCoverage.Result.Value,
                providerResults.FilingCoverage.Result.Value,
                providerResults.TotalRetryCount,
                missingNonCriticalSourceCount,
                reasonerSelection.FallbackOccurred);
            var assessment = DeterministicScoringCalculator.Calculate(scoringInput);
            AddTrace(
                trace,
                WorkflowStep.QualityAssessment,
                TraceStatus.Completed,
                $"Computed evidence score {assessment.EvidenceScore}, directional score {assessment.DirectionalScore}, and contradiction score {assessment.ContradictionScore:0.00}.");

            UpdateProgress(item.JobId, WorkflowStep.AdaptiveBranching.ToString(), warnings, trace);
            ApplyAdaptiveBranching(assessment, warnings, trace);

            UpdateProgress(item.JobId, WorkflowStep.Synthesis.ToString(), warnings, trace);
            var evidence = BuildEvidence(item.Identifier, item.ExecutionMode, providerResults, assessment, warnings);
            var reasoningResult = await reasonerSelection.Reasoner.SynthesizeAsync(evidence, jobToken);
            AddTrace(trace, WorkflowStep.Synthesis, TraceStatus.Completed, $"Generated a {reasoningResult.Signal} thesis with {reasoningResult.Insights.Count} insights.");

            UpdateProgress(item.JobId, WorkflowStep.FinalReflection.ToString(), warnings, trace);
            AddTrace(
                trace,
                WorkflowStep.FinalReflection,
                assessment.UnresolvedContradiction ? TraceStatus.Warning : TraceStatus.Completed,
                assessment.UnresolvedContradiction
                    ? "Final reflection kept confidence conservative because material contradictions remained unresolved."
                    : $"Final reflection confirmed source linkage and confidence {reasoningResult.Confidence}.");

            AddTrace(trace, WorkflowStep.Completion, TraceStatus.Completed, $"Completed research job with signal {reasoningResult.Signal} and confidence {reasoningResult.Confidence}.");
            var completedResult = BuildCompletedResult(item.Identifier, warnings, trace, evidence, reasoningResult, providerResults);

            UpdateRecord(
                item.JobId,
                record => ResearchJobStateMachine.MarkCompleted(
                    record,
                    completedResult.GeneratedAtUtc,
                    completedResult,
                    warnings.ToArray(),
                    trace.ToArray()));

            _logger.LogInformation("Completed research job {JobId} with signal {Signal}", item.JobId, completedResult.Signal);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var (failureWarnings, failureTrace) = GetCurrentFailureState(item.JobId, warnings, trace);
            if (IsTerminal(item.JobId))
            {
                return;
            }

            failureWarnings.Add("Job execution exceeded the configured timeout.");
            AddTrace(failureTrace, WorkflowStep.FinalReflection, TraceStatus.Failed, "Job execution exceeded the configured timeout.");
            FailJob(item.JobId, failureWarnings, failureTrace, "Research job timed out before completion.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed unexpectedly.", item.JobId);
            var (failureWarnings, failureTrace) = GetCurrentFailureState(item.JobId, warnings, trace);
            if (IsTerminal(item.JobId))
            {
                return;
            }

            failureWarnings.Add("The job failed due to an unrecoverable orchestration error.");
            AddTrace(failureTrace, WorkflowStep.FinalReflection, TraceStatus.Failed, "An unrecoverable orchestration error terminated the job.");
            FailJob(item.JobId, failureWarnings, failureTrace, "An unrecoverable orchestration error occurred.");
        }
        finally
        {
            _jobExecutionContextAccessor.Current = null;
        }
    }

    private async Task<ProviderCollectionResult> CollectSourcesAsync(string identifier, CancellationToken cancellationToken)
    {
        var companyProfileTask = ExecuteProviderAsync(
            SourceType.CompanyProfile,
            "company profile",
            ct => _companyProfileProvider.GetAsync(identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure && result.IsRetryable,
            cancellationToken);

        var marketSnapshotTask = ExecuteProviderAsync(
            SourceType.MarketSnapshot,
            "market snapshot",
            ct => _marketSnapshotProvider.GetAsync(identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure,
            cancellationToken);

        var financialStatementTask = ExecuteProviderAsync(
            SourceType.FinancialStatement,
            "financial statements",
            ct => _financialStatementProvider.GetAsync(identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure && result.IsRetryable,
            cancellationToken);

        var newsCoverageTask = ExecuteProviderAsync(
            SourceType.News,
            "news coverage",
            ct => _newsProvider.GetAsync(identifier, ct),
            result => (result.Status == ProviderResultStatus.Empty || result.Status == ProviderResultStatus.TransientFailure) && result.IsRetryable,
            cancellationToken);

        var filingCoverageTask = ExecuteProviderAsync(
            SourceType.FilingExcerpt,
            "filing excerpts",
            ct => _filingExcerptProvider.GetAsync(identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure && result.IsRetryable,
            cancellationToken);

        await Task.WhenAll(companyProfileTask, marketSnapshotTask, financialStatementTask, newsCoverageTask, filingCoverageTask);

        return new ProviderCollectionResult(
            companyProfileTask.Result,
            marketSnapshotTask.Result,
            financialStatementTask.Result,
            newsCoverageTask.Result,
            filingCoverageTask.Result,
            companyProfileTask.Result.RetryCount +
            marketSnapshotTask.Result.RetryCount +
            financialStatementTask.Result.RetryCount +
            newsCoverageTask.Result.RetryCount +
            filingCoverageTask.Result.RetryCount);
    }

    private async Task<ProviderExecutionResult<T>> ExecuteProviderAsync<T>(
        SourceType sourceType,
        string sourceLabel,
        Func<CancellationToken, Task<ProviderResult<T>>> providerCall,
        Func<ProviderResult<T>, bool> shouldRetry,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var traceEvents = new List<ResearchTraceEvent>();
        ProviderResult<T>? result = null;
        var retryCount = 0;

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            result = await ExecuteWithTimeoutAsync(sourceLabel, providerCall, cancellationToken);
            var canRetry = attempt < _options.MaxRetries && shouldRetry(result);
            if (!canRetry)
            {
                break;
            }

            retryCount++;
            traceEvents.Add(
                new ResearchTraceEvent(
                    WorkflowStep.ProviderRetry,
                    TraceStatus.Warning,
                    $"Retrying {sourceLabel} after {result.Status}.",
                    Array.Empty<string>(),
                    _timeProvider.GetUtcNow()));

            await Task.Delay(TimeSpan.FromMilliseconds(_options.RetryDelayMs), _timeProvider, cancellationToken);
        }

        if (retryCount > 0 && result is { Status: ProviderResultStatus.Success })
        {
            warnings.Add($"{Capitalize(sourceLabel)} required retry before successful retrieval.");
        }
        else if (retryCount > 0 && result is not null && result.Status != ProviderResultStatus.Success)
        {
            warnings.Add($"{Capitalize(sourceLabel)} remained unavailable after retry attempts.");
        }

        return new ProviderExecutionResult<T>(sourceType, result!, retryCount, warnings, traceEvents);
    }

    private async Task<ProviderResult<T>> ExecuteWithTimeoutAsync<T>(
        string sourceLabel,
        Func<CancellationToken, Task<ProviderResult<T>>> providerCall,
        CancellationToken cancellationToken)
    {
        using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        providerCts.CancelAfter(TimeSpan.FromMilliseconds(_options.ProviderTimeoutMs));

        try
        {
            return await providerCall(providerCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && providerCts.IsCancellationRequested)
        {
            _logger.LogWarning("Provider call for {SourceLabel} timed out.", sourceLabel);
            return ProviderResult<T>.TransientFailure($"{Capitalize(sourceLabel)} timed out.", true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider call for {SourceLabel} failed.", sourceLabel);
            return ProviderResult<T>.TransientFailure($"{Capitalize(sourceLabel)} failed.", false);
        }
    }

    private void MergeProviderSideEffects(ProviderCollectionResult providerResults, List<string> warnings, List<ResearchTraceEvent> trace)
    {
        var results = new ProviderExecutionResult[]
        {
            providerResults.CompanyProfile,
            providerResults.MarketSnapshot,
            providerResults.FinancialStatement,
            providerResults.NewsCoverage,
            providerResults.FilingCoverage
        };

        foreach (var warning in results.SelectMany(result => result.Warnings))
        {
            warnings.Add(warning);
        }

        foreach (var traceEvent in results.SelectMany(result => result.TraceEvents))
        {
            trace.Add(traceEvent);
        }

        foreach (var result in results)
        {
            AddProviderProvenanceTrace(trace, result);
        }

        AddTrace(
            trace,
            WorkflowStep.ParallelSourceCollection,
            TraceStatus.Completed,
            $"Collected {results.Count(result => result.Status == ProviderResultStatus.Success)} of 5 planned domains.");
    }

    private int HandleNonCriticalAvailability<T>(ProviderExecutionResult<T> executionResult, List<string> warnings, List<ResearchTraceEvent> trace)
    {
        if (executionResult.Result.Status == ProviderResultStatus.Success)
        {
            return 0;
        }

        var warning = $"{executionResult.SourceType} was unavailable after final attempt.";
        warnings.Add(warning);
        AddTrace(trace, WorkflowStep.AdaptiveBranching, TraceStatus.Warning, warning);
        return 1;
    }

    private void ApplyAdaptiveBranching(ResearchAssessment assessment, List<string> warnings, List<ResearchTraceEvent> trace)
    {
        if (assessment.LowNewsMode)
        {
            warnings.Add("Low news coverage reduced narrative confidence.");
            AddTrace(trace, WorkflowStep.AdaptiveBranching, TraceStatus.Warning, "News quality was low, so financial statements and filings were weighted more heavily.");
        }

        if (assessment.UnresolvedContradiction)
        {
            AddTrace(trace, WorkflowStep.AdaptiveBranching, TraceStatus.Warning, "Source disagreement remained unresolved, so confidence stayed conservative.");
        }

        if (assessment.EvidenceScore < 55)
        {
            AddTrace(trace, WorkflowStep.AdaptiveBranching, TraceStatus.Warning, "Evidence score stayed below 55, so the final signal must remain Neutral.");
        }
    }

    private ResearchEvidence BuildEvidence(
        string identifier,
        ResearchExecutionMode executionMode,
        ProviderCollectionResult providerResults,
        ResearchAssessment assessment,
        IReadOnlyList<string> warnings)
    {
        var sourceCatalog = new Dictionary<string, SourceDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [providerResults.CompanyProfile.Result.Value!.SourceId] = new(
                providerResults.CompanyProfile.Result.Value.SourceId,
                SourceType.CompanyProfile,
                providerResults.CompanyProfile.Result.Value.Title,
                providerResults.CompanyProfile.Result.Value.TimestampUtc,
                providerResults.CompanyProfile.Result.Value.TimestampType,
                providerResults.CompanyProfile.Result.Value.QualityScore,
                null,
                providerResults.CompanyProfile.Result.Origin,
                providerResults.CompanyProfile.Result.ProviderName,
                providerResults.CompanyProfile.Result.FallbackOccurred),
            [providerResults.FinancialStatement.Result.Value!.SourceId] = new(
                providerResults.FinancialStatement.Result.Value.SourceId,
                SourceType.FinancialStatement,
                providerResults.FinancialStatement.Result.Value.Title,
                providerResults.FinancialStatement.Result.Value.TimestampUtc,
                providerResults.FinancialStatement.Result.Value.TimestampType,
                providerResults.FinancialStatement.Result.Value.QualityScore,
                null,
                providerResults.FinancialStatement.Result.Origin,
                providerResults.FinancialStatement.Result.ProviderName,
                providerResults.FinancialStatement.Result.FallbackOccurred)
        };

        if (providerResults.MarketSnapshot.Result.Value is not null)
        {
            sourceCatalog[providerResults.MarketSnapshot.Result.Value.SourceId] = new(
                providerResults.MarketSnapshot.Result.Value.SourceId,
                SourceType.MarketSnapshot,
                providerResults.MarketSnapshot.Result.Value.Title,
                providerResults.MarketSnapshot.Result.Value.TimestampUtc,
                providerResults.MarketSnapshot.Result.Value.TimestampType,
                providerResults.MarketSnapshot.Result.Value.QualityScore,
                null,
                providerResults.MarketSnapshot.Result.Origin,
                providerResults.MarketSnapshot.Result.ProviderName,
                providerResults.MarketSnapshot.Result.FallbackOccurred);
        }

        if (providerResults.NewsCoverage.Result.Value is not null)
        {
            foreach (var item in providerResults.NewsCoverage.Result.Value.Items)
            {
                sourceCatalog[item.SourceId] = new(
                    item.SourceId,
                    SourceType.News,
                    item.Headline,
                    item.TimestampUtc,
                    item.TimestampType,
                    item.QualityScore,
                    item.Url,
                    providerResults.NewsCoverage.Result.Origin,
                    providerResults.NewsCoverage.Result.ProviderName,
                    providerResults.NewsCoverage.Result.FallbackOccurred);
            }
        }

        if (providerResults.FilingCoverage.Result.Value is not null)
        {
            foreach (var item in providerResults.FilingCoverage.Result.Value.Items)
            {
                sourceCatalog[item.SourceId] = new(
                    item.SourceId,
                    SourceType.FilingExcerpt,
                    item.Title,
                    item.TimestampUtc,
                    item.TimestampType,
                    item.QualityScore,
                    item.Url,
                    providerResults.FilingCoverage.Result.Origin,
                    providerResults.FilingCoverage.Result.ProviderName,
                    providerResults.FilingCoverage.Result.FallbackOccurred);
            }
        }

        return new ResearchEvidence(
            identifier,
            executionMode,
            providerResults.CompanyProfile.Result.Value!,
            providerResults.MarketSnapshot.Result.Value,
            providerResults.FinancialStatement.Result.Value!,
            providerResults.NewsCoverage.Result.Value,
            providerResults.FilingCoverage.Result.Value,
            assessment,
            warnings.ToArray(),
            sourceCatalog);
    }

    private CompletedResearchResult BuildCompletedResult(
        string identifier,
        IReadOnlyList<string> warnings,
        IReadOnlyList<ResearchTraceEvent> trace,
        ResearchEvidence evidence,
        ReasoningResult reasoningResult,
        ProviderCollectionResult providerResults)
    {
        var citedSourceIds = reasoningResult.ThesisDetails.SourceIds
            .Concat(reasoningResult.InsightDetails.SelectMany(detail => detail.SourceIds))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sourceDetails = citedSourceIds
            .Select(sourceId => evidence.SourceCatalog[sourceId])
            .Select(document => new SourceDetailResponse(
                document.SourceId,
                document.Type,
                document.Title,
                document.TimestampUtc,
                document.TimestampType,
                Math.Round(document.QualityScore, 2, MidpointRounding.AwayFromZero),
                document.Url,
                document.ProviderOrigin,
                document.ProviderName,
                document.FallbackOccurred))
            .ToArray();

        var providerUsage = BuildProviderUsage(providerResults);

        var insightDetails = reasoningResult.InsightDetails
            .Select(detail => new InsightDetailResponse(detail.Insight, detail.SourceIds))
            .ToArray();

        return new CompletedResearchResult(
            reasoningResult.Company,
            identifier,
            reasoningResult.Thesis,
            reasoningResult.Signal,
            reasoningResult.Confidence,
            _timeProvider.GetUtcNow(),
            reasoningResult.Insights,
            citedSourceIds,
            warnings.ToArray(),
            trace.ToArray(),
            sourceDetails,
            providerUsage,
            insightDetails,
            new ThesisDetailResponse(reasoningResult.ThesisDetails.Summary, reasoningResult.ThesisDetails.SourceIds),
            new SignalDetailResponse(
                reasoningResult.SignalDetails.Signal,
                reasoningResult.SignalDetails.Rationale,
                reasoningResult.SignalDetails.SourceIds,
                reasoningResult.SignalDetails.OverrideApplied),
            new ConfidenceDetailResponse(
                reasoningResult.ConfidenceDetails.Rationale,
                reasoningResult.ConfidenceDetails.BoundedFrom,
                reasoningResult.ConfidenceDetails.BoundedTo,
                reasoningResult.ConfidenceDetails.SourceIds),
            new SynthesisDetailResponse(
                reasoningResult.SynthesisDetails.Mode,
                reasoningResult.SynthesisDetails.Model,
                reasoningResult.SynthesisDetails.ValidationRetries,
                reasoningResult.SynthesisDetails.FallbackOccurred),
            new OrchestrationDetailResponse(
                ResearchMode.Standard,
                null,
                null,
                false,
                0,
                0,
                0));
    }

    private void FailCriticalSource(Guid jobId, List<string> warnings, List<ResearchTraceEvent> trace, string message)
    {
        AddTrace(trace, WorkflowStep.AdaptiveBranching, TraceStatus.Failed, message);
        FailJob(jobId, warnings, trace, message);
    }

    private void FailJob(Guid jobId, List<string> warnings, List<ResearchTraceEvent> trace, string message)
    {
        UpdateRecord(
            jobId,
            record => ResearchJobStateMachine.MarkFailed(
                record,
                _timeProvider.GetUtcNow(),
                new ResearchJobError(JobFailedErrorCode, message),
                warnings.ToArray(),
                trace.ToArray()));

        _logger.LogWarning("Job {JobId} failed: {Message}", jobId, message);
    }

    private void UpdateProgress(Guid jobId, string currentStep, List<string> warnings, List<ResearchTraceEvent> trace) =>
        UpdateRecord(
            jobId,
            record => ResearchJobStateMachine.UpdateProgress(
                record,
                currentStep,
                warnings: warnings.ToArray(),
                trace: trace.ToArray()));

    private (List<string> Warnings, List<ResearchTraceEvent> Trace) GetCurrentFailureState(Guid jobId, List<string> warnings, List<ResearchTraceEvent> trace)
    {
        var currentRecord = _jobStore.Get(jobId);
        return currentRecord is null
            ? (warnings, trace)
            : (new List<string>(currentRecord.Warnings), new List<ResearchTraceEvent>(currentRecord.Trace));
    }

    private bool IsTerminal(Guid jobId)
    {
        var currentRecord = _jobStore.Get(jobId);
        return currentRecord?.Status is JobStatus.Completed or JobStatus.Failed;
    }

    private void UpdateRecord(Guid jobId, Func<ResearchJobRecord, ResearchJobRecord> update)
    {
        if (!_jobStore.TryUpdate(jobId, update))
        {
            throw new InvalidOperationException($"Unable to update job {jobId}.");
        }
    }

    private void AddTrace(
        List<ResearchTraceEvent> trace,
        WorkflowStep step,
        TraceStatus status,
        string summary,
        IReadOnlyList<string>? sourceIds = null)
    {
        trace.Add(new ResearchTraceEvent(step, status, summary, sourceIds ?? Array.Empty<string>(), _timeProvider.GetUtcNow()));
    }

    private static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];

    private static IReadOnlyList<ProviderUsageResponse> BuildProviderUsage(ProviderCollectionResult providerResults) =>
        new ProviderExecutionResult[]
        {
            providerResults.CompanyProfile,
            providerResults.MarketSnapshot,
            providerResults.FinancialStatement,
            providerResults.NewsCoverage,
            providerResults.FilingCoverage
        }
        .Select(result => new ProviderUsageResponse(
            result.SourceType,
            result.Status,
            result.Origin,
            result.ProviderName,
            result.FallbackOccurred,
            result.FallbackReason))
        .ToArray();

    private void AddProviderProvenanceTrace(List<ResearchTraceEvent> trace, ProviderExecutionResult result)
    {
        if (result.Status != ProviderResultStatus.Success)
        {
            return;
        }

        if (result.FallbackOccurred)
        {
            AddTrace(
                trace,
                WorkflowStep.ProviderFallback,
                TraceStatus.Warning,
                $"{FormatSourceType(result.SourceType)} used seeded mock fallback via {result.ProviderName}. {result.FallbackReason}");
            return;
        }

        AddTrace(
            trace,
            WorkflowStep.ParallelSourceCollection,
            TraceStatus.Completed,
            $"{FormatSourceType(result.SourceType)} was collected from {DescribeProviderOrigin(result.Origin, result.ProviderName)}.");
    }

    private static string FormatSourceType(SourceType sourceType) =>
        sourceType switch
        {
            SourceType.CompanyProfile => "Company profile",
            SourceType.MarketSnapshot => "Market snapshot",
            SourceType.FinancialStatement => "Financial statements",
            SourceType.News => "News coverage",
            SourceType.FilingExcerpt => "Filing excerpts",
            _ => sourceType.ToString()
        };

    private static string DescribeProviderOrigin(ProviderOrigin origin, string providerName) =>
        origin == ProviderOrigin.Live
            ? $"live provider {providerName}"
            : $"seeded mock provider {providerName}";

    private TimeSpan ResolveEffectiveJobTimeout(ResearchExecutionMode executionMode)
    {
        var configuredTimeout = TimeSpan.FromMilliseconds(Math.Max(_options.JobTimeoutMs, 1000));
        if (executionMode == ResearchExecutionMode.Mock)
        {
            return configuredTimeout;
        }

        var recommendedLiveTimeout = TimeSpan.FromSeconds(90);
        return configuredTimeout >= recommendedLiveTimeout
            ? configuredTimeout
            : recommendedLiveTimeout;
    }

    private abstract record ProviderExecutionResult(
        SourceType SourceType,
        ProviderResultStatus Status,
        ProviderOrigin Origin,
        string ProviderName,
        bool FallbackOccurred,
        string? FallbackReason,
        int RetryCount,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<ResearchTraceEvent> TraceEvents);

    private sealed record ProviderExecutionResult<T>(
        SourceType SourceType,
        ProviderResult<T> Result,
        int RetryCount,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<ResearchTraceEvent> TraceEvents)
        : ProviderExecutionResult(SourceType, Result.Status, Result.Origin, Result.ProviderName, Result.FallbackOccurred, Result.FallbackReason, RetryCount, Warnings, TraceEvents);

    private sealed record ProviderCollectionResult(
        ProviderExecutionResult<CompanyProfile> CompanyProfile,
        ProviderExecutionResult<MarketSnapshot> MarketSnapshot,
        ProviderExecutionResult<FinancialStatement> FinancialStatement,
        ProviderExecutionResult<NewsCoverage> NewsCoverage,
        ProviderExecutionResult<FilingCoverage> FilingCoverage,
        int TotalRetryCount);
}
