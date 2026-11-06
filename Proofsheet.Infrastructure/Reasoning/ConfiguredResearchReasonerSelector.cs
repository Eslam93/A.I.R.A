using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;
using Proofsheet.Domain.Enums;
using Proofsheet.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Proofsheet.Infrastructure.Reasoning;

public sealed class ConfiguredResearchReasonerSelector : IResearchReasonerSelector
{
    private readonly DeterministicResearchReasoner _deterministicReasoner;
    private readonly OpenAiResearchReasoner _openAiResearchReasoner;
    private readonly OpenAiResponsesOptions _options;

    public ConfiguredResearchReasonerSelector(
        DeterministicResearchReasoner deterministicReasoner,
        OpenAiResearchReasoner openAiResearchReasoner,
        IOptions<OpenAiResponsesOptions> options)
    {
        _deterministicReasoner = deterministicReasoner;
        _openAiResearchReasoner = openAiResearchReasoner;
        _options = options.Value;
    }

    public ReasonerSelection Select(ResearchExecutionMode executionMode)
    {
        var configuredModel = ResolveReportModel();

        if (executionMode == ResearchExecutionMode.Mock)
        {
            return new ReasonerSelection(_deterministicReasoner, "deterministic", false, null, null);
        }

        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(configuredModel))
        {
            return executionMode switch
            {
                ResearchExecutionMode.LiveRequired => new ReasonerSelection(
                    null,
                    configuredModel,
                    false,
                    null,
                    "Live report synthesis is not configured. Set OpenAiResponses:Enabled, OpenAiResponses:ApiKey, and OpenAiResponses:ReportModel or OpenAiResponses:Model."),
                _ => new ReasonerSelection(
                    _deterministicReasoner,
                    "deterministic",
                    true,
                    "OpenAI report synthesis was not fully configured. Deterministic synthesis was used instead.",
                    null)
            };
        }

        return new ReasonerSelection(_openAiResearchReasoner, configuredModel, false, null, null);
    }

    public ReasonerSelection Select() => Select(ResearchExecutionMode.Hybrid);

    private string ResolveReportModel() =>
        string.IsNullOrWhiteSpace(_options.ReportModel)
            ? _options.Model
            : _options.ReportModel;
}
