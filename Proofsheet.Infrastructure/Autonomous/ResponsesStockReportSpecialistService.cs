using System.Text.Json;
using System.Text.Json.Nodes;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Models;
using Proofsheet.Infrastructure.Options;
using Proofsheet.Infrastructure.Reasoning;
using Microsoft.Extensions.Options;

namespace Proofsheet.Infrastructure.Autonomous;

public sealed class ResponsesStockReportSpecialistService : IStockReportSpecialistService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IResponsesApi _responsesApi;
    private readonly ISpecialistAgentPromptProvider _promptProvider;
    private readonly DeterministicResearchReasoner _deterministicReasoner;
    private readonly IResearchReasonerSelector _reasonerSelector;
    private readonly OpenAiResponsesOptions _options;

    public ResponsesStockReportSpecialistService(
        IResponsesApi responsesApi,
        ISpecialistAgentPromptProvider promptProvider,
        DeterministicResearchReasoner deterministicResearchReasoner,
        IResearchReasonerSelector reasonerSelector,
        IOptions<OpenAiResponsesOptions> options)
    {
        _responsesApi = responsesApi;
        _promptProvider = promptProvider;
        _deterministicReasoner = deterministicResearchReasoner;
        _reasonerSelector = reasonerSelector;
        _options = options.Value;
    }

    public async Task<ReasoningResult> GenerateAsync(
        AutonomousJobSession session,
        HandoffPacket handoffPacket,
        Func<AutonomousProgressEvent, Task> reportProgressAsync,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var model = ResolveSpecialistModel();
        session.SpecialistModel = model;
        var baseline = await _deterministicReasoner.SynthesizeAsync(handoffPacket.Evidence, cancellationToken);
        var validationErrors = new List<string>();

        await reportProgressAsync(
            new AutonomousProgressEvent(
                Domain.Enums.WorkflowStep.SpecialistDraft,
                Domain.Enums.AgentRole.Specialist,
                "SpecialistSynthesis",
                Domain.Enums.TraceStatus.Completed,
                $"Specialist started drafting the final report using {model}."));

        using var draftResponse = await _responsesApi.CreateResponseAsync(
            BuildDraftRequestBody(model, handoffPacket, baseline, validationErrors),
            _options.TimeoutSeconds,
            cancellationToken);

        var draftText = StructuredReasoningMaterializer.ExtractAssistantText(draftResponse.RootElement);
        if (string.IsNullOrWhiteSpace(draftText))
        {
            throw new InvalidOperationException("Specialist draft was empty.");
        }

        await reportProgressAsync(
            new AutonomousProgressEvent(
                Domain.Enums.WorkflowStep.SpecialistDraft,
                Domain.Enums.AgentRole.Specialist,
                "SpecialistSynthesis",
                Domain.Enums.TraceStatus.Completed,
                "Specialist produced an initial structured draft."));

        var critiqueIssues = new List<string>();
        ReasoningResult? initialResult = null;
        if (!StructuredReasoningMaterializer.TryParse(draftText, out var parsedDraft, out var draftParseError))
        {
            critiqueIssues.Add(draftParseError);
        }
        else if (!StructuredReasoningMaterializer.TryMaterialize(parsedDraft!, handoffPacket.Evidence, baseline, model, 0, out initialResult, out var draftValidationError))
        {
            critiqueIssues.Add(draftValidationError);
        }

        critiqueIssues.AddRange(await CritiqueDraftAsync(model, handoffPacket, draftText, cancellationToken));
        critiqueIssues = critiqueIssues
            .Where(issue => !string.IsNullOrWhiteSpace(issue))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await reportProgressAsync(
            new AutonomousProgressEvent(
                Domain.Enums.WorkflowStep.SpecialistCritique,
                Domain.Enums.AgentRole.Specialist,
                "Validation",
                critiqueIssues.Count == 0 ? Domain.Enums.TraceStatus.Completed : Domain.Enums.TraceStatus.Warning,
                critiqueIssues.Count == 0
                    ? "Specialist critique found the draft internally consistent."
                    : $"Specialist critique flagged {critiqueIssues.Count} issue(s) for repair."));

        if (critiqueIssues.Count == 0 && initialResult is not null)
        {
            await reportProgressAsync(
                new AutonomousProgressEvent(
                    Domain.Enums.WorkflowStep.OutputValidation,
                    Domain.Enums.AgentRole.Specialist,
                    "Validation",
                    Domain.Enums.TraceStatus.Completed,
                    $"Output validation accepted the specialist draft with confidence {initialResult.Confidence}."));
            return initialResult;
        }

        var maxRepairs = Math.Max(0, _options.SpecialistCritiqueRetries);
        for (var repairAttempt = 1; repairAttempt <= maxRepairs; repairAttempt++)
        {
            session.ValidationRetries = repairAttempt;
            using var repairResponse = await _responsesApi.CreateResponseAsync(
                BuildRepairRequestBody(model, handoffPacket, baseline, draftText, critiqueIssues, repairAttempt),
                _options.TimeoutSeconds,
                cancellationToken);

            var repairText = StructuredReasoningMaterializer.ExtractAssistantText(repairResponse.RootElement);
            if (string.IsNullOrWhiteSpace(repairText))
            {
                critiqueIssues = ["The specialist repair attempt returned an empty response."];
                continue;
            }

            if (!StructuredReasoningMaterializer.TryParse(repairText, out var parsedRepair, out var parseError))
            {
                critiqueIssues = [parseError];
                continue;
            }

            if (!StructuredReasoningMaterializer.TryMaterialize(parsedRepair!, handoffPacket.Evidence, baseline, model, repairAttempt, out var repairedResult, out var validationError))
            {
                critiqueIssues = [validationError];
                continue;
            }

            await reportProgressAsync(
                new AutonomousProgressEvent(
                    Domain.Enums.WorkflowStep.OutputValidation,
                    Domain.Enums.AgentRole.Specialist,
                    "Validation",
                    Domain.Enums.TraceStatus.Completed,
                    $"Output validation accepted the repaired specialist draft with confidence {repairedResult!.Confidence}."));
            return repairedResult!;
        }

        await reportProgressAsync(
            new AutonomousProgressEvent(
                Domain.Enums.WorkflowStep.OutputValidation,
                Domain.Enums.AgentRole.Specialist,
                "Validation",
                Domain.Enums.TraceStatus.Failed,
                $"Output validation rejected the specialist result after {Math.Max(1, session.ValidationRetries)} attempt(s)."));

        if (_options.AllowAutonomousStandardFallback && session.ExecutionMode == Domain.Enums.ResearchExecutionMode.Hybrid)
        {
            var selection = _reasonerSelector.Select(session.ExecutionMode);
            if (selection.Reasoner is not null)
            {
                await reportProgressAsync(
                    new AutonomousProgressEvent(
                        Domain.Enums.WorkflowStep.OutputValidation,
                        Domain.Enums.AgentRole.Specialist,
                        "Validation",
                        Domain.Enums.TraceStatus.Warning,
                        "Specialist validation failed, so the autonomous run used the explicitly configured standard synthesis fallback."));
                return await selection.Reasoner.SynthesizeAsync(handoffPacket.Evidence, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Specialist output failed validation: {string.Join(" ", critiqueIssues)}");
    }

    private void EnsureConfigured()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(ResolveSpecialistModel()))
        {
            throw new InvalidOperationException("Autonomous specialist is not configured.");
        }
    }

    private JsonObject BuildDraftRequestBody(
        string model,
        HandoffPacket handoffPacket,
        ReasoningResult baseline,
        IReadOnlyList<string> validationErrors)
    {
        return new JsonObject
        {
            ["model"] = model,
            ["instructions"] = BuildDraftInstructions(handoffPacket, baseline, validationErrors),
            ["input"] = JsonValue.Create("Produce the final structured investment assessment for this handoff packet as JSON only.")
        };
    }

    private JsonObject BuildRepairRequestBody(
        string model,
        HandoffPacket handoffPacket,
        ReasoningResult baseline,
        string priorDraft,
        IReadOnlyList<string> issues,
        int repairAttempt)
    {
        return new JsonObject
        {
            ["model"] = model,
            ["instructions"] = BuildRepairInstructions(handoffPacket, baseline, priorDraft, issues, repairAttempt),
            ["input"] = JsonValue.Create("Return corrected JSON only.")
        };
    }

    private string BuildDraftInstructions(HandoffPacket handoffPacket, ReasoningResult baseline, IReadOnlyList<string> validationErrors)
    {
        var handoffJson = JsonSerializer.Serialize(
            new
            {
                identifier = handoffPacket.Identifier,
                company = handoffPacket.Company,
                warnings = handoffPacket.Warnings,
                providerUsage = handoffPacket.ProviderUsage,
                coordinatorPlanSummary = handoffPacket.CoordinatorPlanSummary,
                unresolvedQuestions = handoffPacket.UnresolvedQuestions,
                missingSourceNotes = handoffPacket.MissingSourceNotes,
                evidence = handoffPacket.Evidence,
                deterministicBaseline = new
                {
                    baseline.Signal,
                    baseline.Confidence,
                    baseline.Thesis,
                    baseline.Insights,
                    baseline.ThesisDetails,
                    baseline.SignalDetails,
                    baseline.ConfidenceDetails
                }
            },
            SerializerOptions);

        var instructions = $"{_promptProvider.GetPrompt().Trim()}\n\nHandoff Packet JSON:\n{handoffJson}";
        if (validationErrors.Count > 0)
        {
            instructions += $"\n\nPrevious validation issues:\n- {string.Join("\n- ", validationErrors)}";
        }

        return instructions;
    }

    private string BuildRepairInstructions(
        HandoffPacket handoffPacket,
        ReasoningResult baseline,
        string priorDraft,
        IReadOnlyList<string> issues,
        int repairAttempt)
    {
        return $"{BuildDraftInstructions(handoffPacket, baseline, Array.Empty<string>())}\n\n" +
               $"Prior draft JSON:\n{priorDraft}\n\n" +
               $"Repair attempt: {repairAttempt}\n" +
               $"Fix these issues and return corrected JSON only:\n- {string.Join("\n- ", issues)}";
    }

    private async Task<IReadOnlyList<string>> CritiqueDraftAsync(
        string model,
        HandoffPacket handoffPacket,
        string draftJson,
        CancellationToken cancellationToken)
    {
        using var critiqueResponse = await _responsesApi.CreateResponseAsync(
            new JsonObject
            {
                ["model"] = model,
                ["instructions"] =
                    "You are the Proofsheet Specialist Critique pass. Review the draft JSON using only the handoff packet. " +
                    "Return JSON only with shape {\"approved\":true|false,\"issues\":[\"issue\"]}. " +
                    "Flag unsupported claims, missing citations, invalid source ids, and signal-confidence mismatch. " +
                    $"Handoff Packet JSON:\n{JsonSerializer.Serialize(new { handoffPacket.Identifier, handoffPacket.Warnings, handoffPacket.UnresolvedQuestions, sourceIds = handoffPacket.Evidence.SourceCatalog.Keys.OrderBy(x => x).ToArray() }, SerializerOptions)}",
                ["input"] = JsonValue.Create($"Draft JSON:\n{draftJson}")
            },
            _options.TimeoutSeconds,
            cancellationToken);

        var critiqueText = StructuredReasoningMaterializer.ExtractAssistantText(critiqueResponse.RootElement);
        if (string.IsNullOrWhiteSpace(critiqueText))
        {
            return ["The specialist critique returned an empty response."];
        }

        try
        {
            var critique = JsonSerializer.Deserialize<CritiqueResponse>(StripCodeFences(critiqueText), SerializerOptions);
            if (critique is null)
            {
                return ["The specialist critique returned null JSON."];
            }

            return critique.Approved
                ? Array.Empty<string>()
                : (critique.Issues ?? Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return [$"The specialist critique could not be parsed: {ex.Message}"];
        }
    }

    private string ResolveSpecialistModel() =>
        string.IsNullOrWhiteSpace(_options.SpecialistModel)
            ? _options.Model
            : _options.SpecialistModel;

    private static string StripCodeFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed.Split('\n');
        return string.Join("\n", lines.Skip(1).SkipLast(1)).Trim();
    }

    private sealed record CritiqueResponse(bool Approved, IReadOnlyList<string>? Issues);
}
