using System.Text.Json;
using System.Text.Json.Nodes;
using Aira.Application.Abstractions;
using Aira.Domain.Contracts;
using Aira.Domain.Enums;
using Aira.Domain.Models;
using Aira.Domain.Services;
using Aira.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Aira.Infrastructure.Reasoning;

public sealed class OpenAiResearchReasoner : IResearchReasoner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IResponsesApi _responsesApi;
    private readonly IReportSynthesisPromptProvider _promptProvider;
    private readonly OpenAiResponsesOptions _options;
    private readonly DeterministicResearchReasoner _deterministicReasoner;

    public OpenAiResearchReasoner(
        IResponsesApi responsesApi,
        IReportSynthesisPromptProvider promptProvider,
        IOptions<OpenAiResponsesOptions> options,
        DeterministicResearchReasoner deterministicResearchReasoner)
    {
        _responsesApi = responsesApi;
        _promptProvider = promptProvider;
        _options = options.Value;
        _deterministicReasoner = deterministicResearchReasoner;
    }

    public async Task<ReasoningResult> SynthesizeAsync(ResearchEvidence evidence, CancellationToken cancellationToken)
    {
        var baseline = await _deterministicReasoner.SynthesizeAsync(evidence, cancellationToken);
        var validationErrors = new List<string>();
        var model = ResolveReportModel();
        var maxAttempts = Math.Max(1, _options.ReportValidationRetries + 1);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var response = await _responsesApi.CreateResponseAsync(
                BuildRequestBody(evidence, baseline, validationErrors),
                _options.TimeoutSeconds,
                cancellationToken);

            var outputText = StructuredReasoningMaterializer.ExtractAssistantText(response.RootElement);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                validationErrors.Add("The model returned an empty response.");
                continue;
            }

            if (!StructuredReasoningMaterializer.TryParse(outputText, out var parsed, out var parseError))
            {
                validationErrors.Add(parseError);
                continue;
            }

            if (StructuredReasoningMaterializer.TryMaterialize(parsed!, evidence, baseline, model, attempt, out var result, out var validationError))
            {
                return result!;
            }

            validationErrors.Add(validationError);
        }

        throw new InvalidOperationException($"Report synthesis validation failed after {maxAttempts} attempts: {string.Join(" ", validationErrors)}");
    }

    private JsonObject BuildRequestBody(ResearchEvidence evidence, ReasoningResult baseline, IReadOnlyList<string> validationErrors)
    {
        return new JsonObject
        {
            ["model"] = ResolveReportModel(),
            ["instructions"] = BuildInstructions(evidence, baseline, validationErrors),
            ["input"] = JsonValue.Create("Produce the final structured investment assessment for this report as JSON only.")
        };
    }

    private string BuildInstructions(ResearchEvidence evidence, ReasoningResult baseline, IReadOnlyList<string> validationErrors)
    {
        var prompt = _promptProvider.GetPrompt();
        var summary = JsonSerializer.Serialize(
            new
            {
                company = evidence.CompanyProfile.CompanyName,
                identifier = evidence.Identifier,
                executionMode = evidence.ExecutionMode.ToString(),
                warnings = evidence.Warnings,
                assessment = new
                {
                    evidence.Assessment.CompletenessScore,
                    evidence.Assessment.EvidenceScore,
                    evidence.Assessment.DirectionalScore,
                    evidence.Assessment.ContradictionScore,
                    evidence.Assessment.AgreementScore,
                    evidence.Assessment.UnresolvedContradiction,
                    evidence.Assessment.LowNewsMode,
                    evidence.Assessment.RetryCount,
                    evidence.Assessment.MissingNonCriticalSourceCount
                },
                companyProfile = new
                {
                    evidence.CompanyProfile.CompanyName,
                    evidence.CompanyProfile.Identifier,
                    evidence.CompanyProfile.Exchange,
                    evidence.CompanyProfile.CikPadded,
                    evidence.CompanyProfile.QualityScore,
                    evidence.CompanyProfile.StanceScore
                },
                marketSnapshot = evidence.MarketSnapshot,
                financialStatement = evidence.FinancialStatement,
                newsCoverage = evidence.NewsCoverage,
                filingCoverage = evidence.FilingCoverage,
                sourceCatalog = evidence.SourceCatalog.Values.OrderBy(value => value.SourceId).ToArray(),
                deterministicBaseline = new
                {
                    baseline.Signal,
                    baseline.Confidence,
                    baseline.Thesis,
                    baseline.Insights,
                    thesisDetails = baseline.ThesisDetails,
                    insightDetails = baseline.InsightDetails
                },
                requiredJsonShape = new
                {
                    thesis = "string",
                    signal = "Bullish|Neutral|Bearish",
                    confidence = 0,
                    insights = new[] { "string", "string" },
                    thesisDetails = new { summary = "string", sourceIds = new[] { "sourceId" } },
                    insightDetails = new[] { new { insight = "string", sourceIds = new[] { "sourceId" } } },
                    signalDetails = new { rationale = "string", sourceIds = new[] { "sourceId" } },
                    confidenceDetails = new { rationale = "string", sourceIds = new[] { "sourceId" } }
                }
            },
            SerializerOptions);

        var instructions = $"{prompt.Trim()}\n\nEvidence Package JSON:\n{summary}";
        if (validationErrors.Count > 0)
        {
            instructions += $"\n\nPrevious output failed validation. Fix these issues and return corrected JSON only:\n- {string.Join("\n- ", validationErrors)}";
        }

        return instructions;
    }

    private string ResolveReportModel() =>
        string.IsNullOrWhiteSpace(_options.ReportModel)
            ? _options.Model
            : _options.ReportModel;
}
