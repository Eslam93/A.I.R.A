using Aira.Application.Abstractions;
using Aira.Application.Models;
using Aira.Domain.Enums;
using Aira.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Aira.Infrastructure.Services;

public sealed class ResearchExecutionModeResolver : IResearchExecutionModeResolver
{
    private readonly ResearchExecutionOptions _executionOptions;
    private readonly OpenAiResponsesOptions _responsesOptions;
    private readonly SecEdgarOptions _secOptions;
    private readonly TwelveDataOptions _twelveDataOptions;
    private readonly AlphaVantageOptions _alphaVantageOptions;

    public ResearchExecutionModeResolver(
        IOptions<ResearchExecutionOptions> executionOptions,
        IOptions<OpenAiResponsesOptions> responsesOptions,
        IOptions<SecEdgarOptions> secOptions,
        IOptions<TwelveDataOptions> twelveDataOptions,
        IOptions<AlphaVantageOptions> alphaVantageOptions)
    {
        _executionOptions = executionOptions.Value;
        _responsesOptions = responsesOptions.Value;
        _secOptions = secOptions.Value;
        _twelveDataOptions = twelveDataOptions.Value;
        _alphaVantageOptions = alphaVantageOptions.Value;
    }

    public ResearchExecutionMode Resolve()
    {
        if (_executionOptions.Mode != ResearchExecutionMode.Auto)
        {
            return _executionOptions.Mode;
        }

        return AreLiveProvidersConfigured() && IsReportSynthesisConfigured()
            ? ResearchExecutionMode.LiveRequired
            : ResearchExecutionMode.Mock;
    }

    private bool AreLiveProvidersConfigured() =>
        _secOptions.Enabled &&
        !string.IsNullOrWhiteSpace(_secOptions.UserAgent) &&
        _twelveDataOptions.Enabled &&
        !string.IsNullOrWhiteSpace(_twelveDataOptions.ApiKey) &&
        _alphaVantageOptions.Enabled &&
        !string.IsNullOrWhiteSpace(_alphaVantageOptions.ApiKey);

    private bool IsReportSynthesisConfigured()
    {
        var model = string.IsNullOrWhiteSpace(_responsesOptions.ReportModel)
            ? _responsesOptions.Model
            : _responsesOptions.ReportModel;

        return _responsesOptions.Enabled
            && !string.IsNullOrWhiteSpace(_responsesOptions.ApiKey)
            && !string.IsNullOrWhiteSpace(model);
    }
}
