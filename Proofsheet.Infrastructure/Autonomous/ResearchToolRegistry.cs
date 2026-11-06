using System.Text.Json;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Contracts;
using Proofsheet.Domain.Enums;
using Proofsheet.Domain.Models;
using Proofsheet.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Proofsheet.Infrastructure.Autonomous;

public sealed class ResearchToolRegistry : IResearchToolRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ICompanyProfileProvider _companyProfileProvider;
    private readonly IMarketSnapshotProvider _marketSnapshotProvider;
    private readonly IFinancialStatementProvider _financialStatementProvider;
    private readonly INewsProvider _newsProvider;
    private readonly IFilingExcerptProvider _filingExcerptProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ResearchExecutionOptions _options;
    private readonly ILogger<ResearchToolRegistry> _logger;

    public ResearchToolRegistry(
        ICompanyProfileProvider companyProfileProvider,
        IMarketSnapshotProvider marketSnapshotProvider,
        IFinancialStatementProvider financialStatementProvider,
        INewsProvider newsProvider,
        IFilingExcerptProvider filingExcerptProvider,
        TimeProvider timeProvider,
        IOptions<ResearchExecutionOptions> options,
        ILogger<ResearchToolRegistry> logger)
    {
        _companyProfileProvider = companyProfileProvider;
        _marketSnapshotProvider = marketSnapshotProvider;
        _financialStatementProvider = financialStatementProvider;
        _newsProvider = newsProvider;
        _filingExcerptProvider = filingExcerptProvider;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(
        AutonomousJobSession session,
        string toolName,
        string arguments,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        session.ToolCallCount++;
        session.ToolHistory.Add(toolName);

        return toolName switch
        {
            "resolveTicker" => await ResolveTickerAsync(session, reportProgressAsync),
            "getCompanyProfile" => await GetCompanyProfileAsync(session, reportProgressAsync, cancellationToken),
            "getMarketSnapshot" => await GetMarketSnapshotAsync(session, reportProgressAsync, cancellationToken),
            "getFinancialStatements" => await GetFinancialStatementsAsync(session, reportProgressAsync, cancellationToken),
            "getNewsCoverage" => await GetNewsCoverageAsync(session, reportProgressAsync, cancellationToken),
            "getFilingCoverage" => await GetFilingCoverageAsync(session, reportProgressAsync, cancellationToken),
            "getEvidenceStatus" => await GetEvidenceStatusAsync(session, reportProgressAsync),
            "handoffToSpecialist" => await HandoffToSpecialistAsync(session, reportProgressAsync),
            _ => JsonSerializer.Serialize(new { error = "unsupported_tool", toolName }, SerializerOptions)
        };
    }

    private async Task<string> ResolveTickerAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync)
    {
        session.Plan ??= new CoordinatorPlan(
            ["resolveTicker", "getCompanyProfile", "getMarketSnapshot", "getFinancialStatements", "getNewsCoverage", "getFilingCoverage", "getEvidenceStatus", "handoffToSpecialist"],
            "Coordinator is dynamically assembling evidence across profile, market, fundamentals, news, filings, evidence review, and specialist handoff.");

        await reportProgressAsync(
            new AutonomousProgressEvent(
                WorkflowStep.CoordinatorPlanning,
                AgentRole.Coordinator,
                "Planning",
                TraceStatus.Completed,
                $"Coordinator resolved {session.Identifier} to {session.ResolvedTicker.CompanyName} and established a bounded tool plan."));

        return JsonSerializer.Serialize(
            new
            {
                symbol = session.ResolvedTicker.Symbol,
                companyName = session.ResolvedTicker.CompanyName,
                session.ResolvedTicker.Exchange,
                session.ResolvedTicker.CikPadded,
                executionMode = session.ExecutionMode.ToString(),
                availableTools = session.Plan.PlannedTools
            },
            SerializerOptions);
    }

    private Task<string> GetCompanyProfileAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync, CancellationToken cancellationToken) =>
        ExecuteSourceToolAsync(
            session,
            SourceType.CompanyProfile,
            "company profile",
            ct => _companyProfileProvider.GetAsync(session.Identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure && result.IsRetryable,
            result => session.CompanyProfile = result,
            reportProgressAsync,
            cancellationToken);

    private Task<string> GetMarketSnapshotAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync, CancellationToken cancellationToken) =>
        ExecuteSourceToolAsync(
            session,
            SourceType.MarketSnapshot,
            "market snapshot",
            ct => _marketSnapshotProvider.GetAsync(session.Identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure,
            result => session.MarketSnapshot = result,
            reportProgressAsync,
            cancellationToken);

    private Task<string> GetFinancialStatementsAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync, CancellationToken cancellationToken) =>
        ExecuteSourceToolAsync(
            session,
            SourceType.FinancialStatement,
            "financial statements",
            ct => _financialStatementProvider.GetAsync(session.Identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure && result.IsRetryable,
            result => session.FinancialStatement = result,
            reportProgressAsync,
            cancellationToken);

    private Task<string> GetNewsCoverageAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync, CancellationToken cancellationToken) =>
        ExecuteSourceToolAsync(
            session,
            SourceType.News,
            "news coverage",
            ct => _newsProvider.GetAsync(session.Identifier, ct),
            result => (result.Status == ProviderResultStatus.Empty || result.Status == ProviderResultStatus.TransientFailure) && result.IsRetryable,
            result => session.NewsCoverage = result,
            reportProgressAsync,
            cancellationToken);

    private Task<string> GetFilingCoverageAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync, CancellationToken cancellationToken) =>
        ExecuteSourceToolAsync(
            session,
            SourceType.FilingExcerpt,
            "filing coverage",
            ct => _filingExcerptProvider.GetAsync(session.Identifier, ct),
            result => result.Status == ProviderResultStatus.TransientFailure && result.IsRetryable,
            result => session.FilingCoverage = result,
            reportProgressAsync,
            cancellationToken);

    private async Task<string> ExecuteSourceToolAsync<T>(
        AutonomousJobSession session,
        SourceType sourceType,
        string sourceLabel,
        Func<CancellationToken, Task<ProviderResult<T>>> providerCall,
        Func<ProviderResult<T>, bool> shouldRetry,
        Action<AutonomousSourceResult<T>> assign,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        var executionResult = await ExecuteProviderAsync(sourceType, sourceLabel, providerCall, shouldRetry, reportProgressAsync, cancellationToken);
        assign(executionResult);
        session.Warnings.AddRange(executionResult.Warnings);

        return JsonSerializer.Serialize(
            new
            {
                domain = sourceType.ToString(),
                attempted = executionResult.Attempted,
                status = executionResult.Status.ToString(),
                origin = executionResult.Origin.ToString(),
                providerName = executionResult.ProviderName,
                executionResult.FallbackOccurred,
                executionResult.FallbackReason,
                retryCount = executionResult.RetryCount,
                error = executionResult.Result?.ErrorMessage,
                value = executionResult.Result is null ? null : (object?)executionResult.Result.Value
            },
            SerializerOptions);
    }

    private async Task<string> GetEvidenceStatusAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync)
    {
        var review = BuildEvidenceGapReview(session);
        session.LatestEvidenceGapReview = review;
        session.ReflectionRounds++;

        await reportProgressAsync(
            new AutonomousProgressEvent(
                WorkflowStep.EvidenceGapReview,
                AgentRole.Coordinator,
                "EvidenceReview",
                review.ReadyForHandoff ? TraceStatus.Completed : TraceStatus.Warning,
                review.Summary));

        var assessment = TryBuildAssessment(session);
        if (assessment is not null)
        {
            await reportProgressAsync(
                new AutonomousProgressEvent(
                    WorkflowStep.ContradictionReview,
                    AgentRole.Coordinator,
                    "EvidenceReview",
                    assessment.UnresolvedContradiction ? TraceStatus.Warning : TraceStatus.Completed,
                    assessment.UnresolvedContradiction
                        ? $"Contradiction review found unresolved disagreement at {assessment.ContradictionScore:0.00}."
                        : $"Contradiction review found agreement score {assessment.AgreementScore}."));
        }

        return JsonSerializer.Serialize(
            new
            {
                readyForHandoff = review.ReadyForHandoff,
                missingRequiredDomains = review.MissingRequiredDomains,
                missingAttemptDomains = review.MissingAttemptDomains,
                concerns = review.Concerns,
                assessment = assessment is null
                    ? null
                    : new
                    {
                        assessment.CompletenessScore,
                        assessment.EvidenceScore,
                        assessment.DirectionalScore,
                        assessment.ContradictionScore,
                        assessment.AgreementScore,
                        assessment.UnresolvedContradiction,
                        assessment.LowNewsMode
                    }
            },
            SerializerOptions);
    }

    private async Task<string> HandoffToSpecialistAsync(AutonomousJobSession session, Func<AutonomousProgressEvent, Task> reportProgressAsync)
    {
        var review = BuildEvidenceGapReview(session);
        session.LatestEvidenceGapReview = review;
        session.ReflectionRounds++;

        await reportProgressAsync(
            new AutonomousProgressEvent(
                WorkflowStep.EvidenceGapReview,
                AgentRole.Coordinator,
                "EvidenceReview",
                review.ReadyForHandoff ? TraceStatus.Completed : TraceStatus.Warning,
                review.Summary));

        var assessment = TryBuildAssessment(session);
        if (assessment is not null)
        {
            await reportProgressAsync(
                new AutonomousProgressEvent(
                    WorkflowStep.ContradictionReview,
                    AgentRole.Coordinator,
                    "EvidenceReview",
                    assessment.UnresolvedContradiction ? TraceStatus.Warning : TraceStatus.Completed,
                    assessment.UnresolvedContradiction
                        ? $"Contradiction review found unresolved disagreement at {assessment.ContradictionScore:0.00}."
                        : $"Contradiction review found agreement score {assessment.AgreementScore}."));
        }

        if (!review.ReadyForHandoff || assessment is null)
        {
            return JsonSerializer.Serialize(
                new
                {
                    ready = false,
                    issues = review.Concerns.Concat(review.MissingRequiredDomains).Concat(review.MissingAttemptDomains).Distinct().ToArray()
                },
                SerializerOptions);
        }

        var evidence = BuildEvidence(session, assessment);
        var missingSourceNotes = BuildMissingSourceNotes(session);
        var unresolvedQuestions = BuildUnresolvedQuestions(review, assessment, missingSourceNotes);

        session.Plan ??= new CoordinatorPlan(
            session.ToolHistory.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            $"Coordinator executed {string.Join(", ", session.ToolHistory.Distinct(StringComparer.OrdinalIgnoreCase))} before specialist handoff.");
        session.HandoffPacket = new HandoffPacket(
            session.Identifier,
            session.ResolvedTicker,
            evidence,
            session.Warnings.ToArray(),
            BuildProviderUsage(session),
            session.Plan.Summary,
            unresolvedQuestions,
            missingSourceNotes);
        session.HandoffOccurred = true;

        await reportProgressAsync(
            new AutonomousProgressEvent(
                WorkflowStep.SpecialistHandoff,
                AgentRole.Coordinator,
                "Handoff",
                TraceStatus.Completed,
                $"Coordinator handed off an evidence package with {evidence.SourceCatalog.Count} sources to the specialist."));

        return JsonSerializer.Serialize(
            new
            {
                ready = true,
                coordinatorPlanSummary = session.Plan.Summary,
                sourceCount = evidence.SourceCatalog.Count,
                warnings = session.Warnings
            },
            SerializerOptions);
    }

    private async Task<AutonomousSourceResult<T>> ExecuteProviderAsync<T>(
        SourceType sourceType,
        string sourceLabel,
        Func<CancellationToken, Task<ProviderResult<T>>> providerCall,
        Func<ProviderResult<T>, bool> shouldRetry,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
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
            var retrySummary = $"Retrying {sourceLabel} after {result.Status}.";
            traceEvents.Add(new ResearchTraceEvent(WorkflowStep.ProviderRetry, TraceStatus.Warning, retrySummary, Array.Empty<string>(), _timeProvider.GetUtcNow()));
            await reportProgressAsync(
                new AutonomousProgressEvent(
                    WorkflowStep.ProviderRetry,
                    AgentRole.Coordinator,
                    "ToolExecution",
                    TraceStatus.Warning,
                    retrySummary));
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

        var summary = BuildSourceSummary(sourceType, sourceLabel, result!);
        var step = result!.FallbackOccurred ? WorkflowStep.ProviderFallback : WorkflowStep.CoordinatorToolExecution;
        var status = result.Status == ProviderResultStatus.Success
            ? (result.FallbackOccurred ? TraceStatus.Warning : TraceStatus.Completed)
            : TraceStatus.Warning;
        traceEvents.Add(new ResearchTraceEvent(step, status, summary, Array.Empty<string>(), _timeProvider.GetUtcNow()));
        await reportProgressAsync(
            new AutonomousProgressEvent(
                step,
                AgentRole.Coordinator,
                "ToolExecution",
                status,
                summary));

        return new AutonomousSourceResult<T>(sourceType, true, result, retryCount, warnings, traceEvents);
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
            _logger.LogWarning("Autonomous provider call for {SourceLabel} timed out.", sourceLabel);
            return ProviderResult<T>.TransientFailure($"{Capitalize(sourceLabel)} timed out.", true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Autonomous provider call for {SourceLabel} failed.", sourceLabel);
            return ProviderResult<T>.TransientFailure($"{Capitalize(sourceLabel)} failed.", false);
        }
    }

    private static string BuildSourceSummary<T>(SourceType sourceType, string sourceLabel, ProviderResult<T> result)
    {
        if (result.Status != ProviderResultStatus.Success)
        {
            return $"{FormatSourceType(sourceType)} finished with {result.Status}.";
        }

        if (result.FallbackOccurred)
        {
            return $"{FormatSourceType(sourceType)} used seeded mock fallback via {result.ProviderName}. {result.FallbackReason}";
        }

        return $"{FormatSourceType(sourceType)} was collected from {DescribeProviderOrigin(result.Origin, result.ProviderName)}.";
    }

    private static EvidenceGapReview BuildEvidenceGapReview(AutonomousJobSession session)
    {
        var missingRequired = new List<string>();
        if (!session.CompanyProfile.IsSuccess)
        {
            missingRequired.Add(nameof(SourceType.CompanyProfile));
        }

        if (!session.FinancialStatement.IsSuccess)
        {
            missingRequired.Add(nameof(SourceType.FinancialStatement));
        }

        if (!session.NewsCoverage.IsSuccess && !session.FilingCoverage.IsSuccess)
        {
            missingRequired.Add("NewsCoverageOrFilingCoverage");
        }

        var missingAttempts = new List<string>();
        if (!session.CompanyProfile.Attempted)
        {
            missingAttempts.Add(nameof(SourceType.CompanyProfile));
        }

        if (!session.MarketSnapshot.Attempted)
        {
            missingAttempts.Add(nameof(SourceType.MarketSnapshot));
        }

        if (!session.FinancialStatement.Attempted)
        {
            missingAttempts.Add(nameof(SourceType.FinancialStatement));
        }

        if (!session.NewsCoverage.Attempted)
        {
            missingAttempts.Add(nameof(SourceType.News));
        }

        if (!session.FilingCoverage.Attempted)
        {
            missingAttempts.Add(nameof(SourceType.FilingExcerpt));
        }

        var concerns = new List<string>();
        var assessment = TryBuildAssessment(session);
        if (assessment is not null)
        {
            if (assessment.LowNewsMode)
            {
                concerns.Add("Low news quality keeps narrative confidence conservative.");
            }

            if (assessment.UnresolvedContradiction)
            {
                concerns.Add("Material contradictions remain unresolved.");
            }
        }

        var ready = missingRequired.Count == 0 && missingAttempts.Count == 0;
        var summary = ready
            ? "Evidence review found the package ready for specialist handoff."
            : $"Evidence review found gaps. Missing required: {FormatList(missingRequired)}. Missing attempts: {FormatList(missingAttempts)}.";

        return new EvidenceGapReview(ready, missingRequired.ToArray(), missingAttempts.ToArray(), concerns.ToArray(), summary);
    }

    private static ResearchAssessment? TryBuildAssessment(AutonomousJobSession session)
    {
        if (!session.CompanyProfile.IsSuccess || !session.FinancialStatement.IsSuccess)
        {
            return null;
        }

        var missingNonCriticalSourceCount = 0;
        if (!session.MarketSnapshot.IsSuccess)
        {
            missingNonCriticalSourceCount++;
        }

        if (!session.NewsCoverage.IsSuccess)
        {
            missingNonCriticalSourceCount++;
        }

        if (!session.FilingCoverage.IsSuccess)
        {
            missingNonCriticalSourceCount++;
        }

        return DeterministicScoringCalculator.Calculate(
            new ResearchScoringInput(
                session.CompanyProfile.Result!.Value!,
                session.MarketSnapshot.Result?.Value,
                session.FinancialStatement.Result!.Value!,
                session.NewsCoverage.Result?.Value,
                session.FilingCoverage.Result?.Value,
                session.CompanyProfile.RetryCount + session.MarketSnapshot.RetryCount + session.FinancialStatement.RetryCount + session.NewsCoverage.RetryCount + session.FilingCoverage.RetryCount,
                missingNonCriticalSourceCount,
                session.CompanyProfile.FallbackOccurred || session.MarketSnapshot.FallbackOccurred || session.FinancialStatement.FallbackOccurred || session.NewsCoverage.FallbackOccurred || session.FilingCoverage.FallbackOccurred));
    }

    private static ResearchEvidence BuildEvidence(AutonomousJobSession session, ResearchAssessment assessment)
    {
        var sourceCatalog = new Dictionary<string, SourceDocument>(StringComparer.OrdinalIgnoreCase)
        {
            [session.CompanyProfile.Result!.Value!.SourceId] = new(
                session.CompanyProfile.Result.Value.SourceId,
                SourceType.CompanyProfile,
                session.CompanyProfile.Result.Value.Title,
                session.CompanyProfile.Result.Value.TimestampUtc,
                session.CompanyProfile.Result.Value.TimestampType,
                session.CompanyProfile.Result.Value.QualityScore,
                null,
                session.CompanyProfile.Origin,
                session.CompanyProfile.ProviderName,
                session.CompanyProfile.FallbackOccurred),
            [session.FinancialStatement.Result!.Value!.SourceId] = new(
                session.FinancialStatement.Result.Value.SourceId,
                SourceType.FinancialStatement,
                session.FinancialStatement.Result.Value.Title,
                session.FinancialStatement.Result.Value.TimestampUtc,
                session.FinancialStatement.Result.Value.TimestampType,
                session.FinancialStatement.Result.Value.QualityScore,
                null,
                session.FinancialStatement.Origin,
                session.FinancialStatement.ProviderName,
                session.FinancialStatement.FallbackOccurred)
        };

        if (session.MarketSnapshot.Result?.Value is not null)
        {
            var market = session.MarketSnapshot.Result.Value;
            sourceCatalog[market.SourceId] = new SourceDocument(
                market.SourceId,
                SourceType.MarketSnapshot,
                market.Title,
                market.TimestampUtc,
                market.TimestampType,
                market.QualityScore,
                null,
                session.MarketSnapshot.Origin,
                session.MarketSnapshot.ProviderName,
                session.MarketSnapshot.FallbackOccurred);
        }

        if (session.NewsCoverage.Result?.Value is not null)
        {
            foreach (var item in session.NewsCoverage.Result.Value.Items)
            {
                sourceCatalog[item.SourceId] = new SourceDocument(
                    item.SourceId,
                    SourceType.News,
                    item.Headline,
                    item.TimestampUtc,
                    item.TimestampType,
                    item.QualityScore,
                    item.Url,
                    session.NewsCoverage.Origin,
                    session.NewsCoverage.ProviderName,
                    session.NewsCoverage.FallbackOccurred);
            }
        }

        if (session.FilingCoverage.Result?.Value is not null)
        {
            foreach (var item in session.FilingCoverage.Result.Value.Items)
            {
                sourceCatalog[item.SourceId] = new SourceDocument(
                    item.SourceId,
                    SourceType.FilingExcerpt,
                    item.Title,
                    item.TimestampUtc,
                    item.TimestampType,
                    item.QualityScore,
                    item.Url,
                    session.FilingCoverage.Origin,
                    session.FilingCoverage.ProviderName,
                    session.FilingCoverage.FallbackOccurred);
            }
        }

        return new ResearchEvidence(
            session.Identifier,
            session.ExecutionMode,
            session.CompanyProfile.Result!.Value!,
            session.MarketSnapshot.Result?.Value,
            session.FinancialStatement.Result!.Value!,
            session.NewsCoverage.Result?.Value,
            session.FilingCoverage.Result?.Value,
            assessment,
            session.Warnings.ToArray(),
            sourceCatalog);
    }

    private static IReadOnlyList<string> BuildMissingSourceNotes(AutonomousJobSession session)
    {
        var notes = new List<string>();
        if (!session.MarketSnapshot.IsSuccess)
        {
            notes.Add("Market snapshot was unavailable.");
        }

        if (!session.NewsCoverage.IsSuccess)
        {
            notes.Add("News coverage was unavailable or insufficient.");
        }

        if (!session.FilingCoverage.IsSuccess)
        {
            notes.Add("Filing coverage was unavailable.");
        }

        return notes.ToArray();
    }

    private static IReadOnlyList<string> BuildUnresolvedQuestions(EvidenceGapReview review, ResearchAssessment assessment, IReadOnlyList<string> missingSourceNotes)
    {
        var items = new List<string>();
        items.AddRange(review.Concerns);
        items.AddRange(missingSourceNotes);

        if (assessment.UnresolvedContradiction)
        {
            items.Add("Directional evidence remained mixed after contradiction review.");
        }

        return items.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<ProviderUsageResponse> BuildProviderUsage(AutonomousJobSession session)
    {
        var usage = new List<ProviderUsageResponse>();

        AddUsage(usage, session.CompanyProfile);
        AddUsage(usage, session.MarketSnapshot);
        AddUsage(usage, session.FinancialStatement);
        AddUsage(usage, session.NewsCoverage);
        AddUsage(usage, session.FilingCoverage);

        return usage;
    }

    private static void AddUsage<T>(List<ProviderUsageResponse> usage, AutonomousSourceResult<T> source)
    {
        if (!source.Attempted || source.Result is null)
        {
            return;
        }

        usage.Add(
            new ProviderUsageResponse(
                source.SourceType,
                source.Result.Status,
                source.Result.Origin,
                source.Result.ProviderName,
                source.Result.FallbackOccurred,
                source.Result.FallbackReason));
    }

    private static string FormatList(IReadOnlyCollection<string> items) =>
        items.Count == 0 ? "none" : string.Join(", ", items);

    private static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];

    private static string FormatSourceType(SourceType sourceType) =>
        sourceType switch
        {
            SourceType.CompanyProfile => "Company profile",
            SourceType.MarketSnapshot => "Market snapshot",
            SourceType.FinancialStatement => "Financial statements",
            SourceType.News => "News coverage",
            SourceType.FilingExcerpt => "Filing coverage",
            _ => sourceType.ToString()
        };

    private static string DescribeProviderOrigin(ProviderOrigin origin, string providerName) =>
        origin == ProviderOrigin.Live
            ? $"live provider {providerName}"
            : $"seeded mock provider {providerName}";
}
