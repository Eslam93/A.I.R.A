using System.Text.Json;
using Aira.Domain.Enums;
using Aira.Domain.Models;
using Aira.Domain.Services;

namespace Aira.Infrastructure.Reasoning;

internal static class StructuredReasoningMaterializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static bool TryParse(string content, out ModelSynthesisResponse? parsed, out string error)
    {
        try
        {
            parsed = JsonSerializer.Deserialize<ModelSynthesisResponse>(StripCodeFences(content), SerializerOptions);
            error = parsed is null ? "The model returned null JSON." : string.Empty;
            return parsed is not null;
        }
        catch (Exception ex)
        {
            parsed = null;
            error = $"The model response could not be parsed as JSON: {ex.Message}";
            return false;
        }
    }

    public static bool TryMaterialize(
        ModelSynthesisResponse parsed,
        ResearchEvidence evidence,
        ReasoningResult baseline,
        string model,
        int validationRetryCount,
        out ReasoningResult? result,
        out string validationError)
    {
        result = null;
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(parsed.Thesis))
        {
            validationError = "Thesis is required.";
            return false;
        }

        if (!Enum.TryParse<InvestmentSignal>(parsed.Signal, true, out var parsedSignal))
        {
            validationError = "Signal must be Bullish, Neutral, or Bearish.";
            return false;
        }

        if (parsed.Confidence < 0 || parsed.Confidence > 100)
        {
            validationError = "Confidence must be between 0 and 100.";
            return false;
        }

        var cleanedInsights = parsed.Insights?
            .Where(insight => !string.IsNullOrWhiteSpace(insight))
            .Select(insight => insight.Trim())
            .ToArray() ?? [];
        if (cleanedInsights.Length < 2 || cleanedInsights.Length > 5)
        {
            validationError = "Insights must contain between 2 and 5 items.";
            return false;
        }

        if (parsed.InsightDetails is null || parsed.InsightDetails.Count != cleanedInsights.Length)
        {
            validationError = "Insight details must match the insight count exactly.";
            return false;
        }

        var validSourceIds = evidence.SourceCatalog.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!HasValidSources(parsed.ThesisDetails?.SourceIds, validSourceIds, Math.Min(2, validSourceIds.Count)))
        {
            validationError = "Thesis details must reference valid source ids.";
            return false;
        }

        if (!HasValidSources(parsed.SignalDetails?.SourceIds, validSourceIds, 1))
        {
            validationError = "Signal details must reference valid source ids.";
            return false;
        }

        if (!HasValidSources(parsed.ConfidenceDetails?.SourceIds, validSourceIds, 1))
        {
            validationError = "Confidence details must reference valid source ids.";
            return false;
        }

        var insightReferences = new List<InsightReference>(cleanedInsights.Length);
        for (var index = 0; index < cleanedInsights.Length; index++)
        {
            var detail = parsed.InsightDetails[index];
            if (!HasValidSources(detail.SourceIds, validSourceIds, 1))
            {
                validationError = $"Insight detail {index + 1} must reference valid source ids.";
                return false;
            }

            insightReferences.Add(new InsightReference(cleanedInsights[index], detail.SourceIds!.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        var thesisReference = new ThesisReference(
            Truncate(parsed.ThesisDetails!.Summary, 240),
            parsed.ThesisDetails.SourceIds!.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        var supportCoverageScore = DeterministicScoringCalculator.CalculateSupportCoverageScore(
            thesisReference,
            insightReferences,
            evidence.SourceCatalog.Keys.ToArray());

        var baselineConfidence = DeterministicScoringCalculator.CalculateConfidence(evidence.Assessment, supportCoverageScore);
        var lowerBound = Math.Max(0, baselineConfidence - 10);
        var upperBound = Math.Min(100, baselineConfidence + 10);
        var boundedConfidence = Math.Clamp(parsed.Confidence, lowerBound, upperBound);

        var overrideApplied = evidence.Assessment.EvidenceScore < 55 && parsedSignal != InvestmentSignal.Neutral;
        var finalSignal = overrideApplied ? InvestmentSignal.Neutral : parsedSignal;

        result = new ReasoningResult(
            evidence.CompanyProfile.CompanyName,
            parsed.Thesis.Trim(),
            finalSignal,
            boundedConfidence,
            cleanedInsights,
            insightReferences,
            thesisReference,
            new SignalReference(
                finalSignal,
                string.IsNullOrWhiteSpace(parsed.SignalDetails!.Rationale)
                    ? baseline.SignalDetails.Rationale
                    : parsed.SignalDetails.Rationale.Trim(),
                parsed.SignalDetails.SourceIds!.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                overrideApplied),
            new ConfidenceReference(
                string.IsNullOrWhiteSpace(parsed.ConfidenceDetails!.Rationale)
                    ? baseline.ConfidenceDetails.Rationale
                    : parsed.ConfidenceDetails.Rationale.Trim(),
                parsed.Confidence,
                boundedConfidence,
                parsed.ConfidenceDetails.SourceIds!.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            new SynthesisMetadata(evidence.ExecutionMode, model, validationRetryCount, false));
        return true;
    }

    public static string ExtractAssistantText(JsonElement response)
    {
        if (response.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!response.TryGetProperty("output", out var outputElement) || outputElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var messages = new List<string>();
        foreach (var item in outputElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement)
                && string.Equals(typeElement.GetString(), "message", StringComparison.OrdinalIgnoreCase)
                && item.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (!contentItem.TryGetProperty("type", out var contentTypeElement))
                    {
                        continue;
                    }

                    var contentType = contentTypeElement.GetString() ?? string.Empty;
                    if ((string.Equals(contentType, "output_text", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(contentType, "text", StringComparison.OrdinalIgnoreCase))
                        && contentItem.TryGetProperty("text", out var textElement))
                    {
                        var text = textElement.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            messages.Add(text);
                        }
                    }
                }
            }
        }

        return string.Join("\n\n", messages).Trim();
    }

    private static bool HasValidSources(IReadOnlyList<string>? sourceIds, ISet<string> validSourceIds, int minimumCount)
    {
        if (sourceIds is null || sourceIds.Count < minimumCount)
        {
            return false;
        }

        return sourceIds.All(sourceId => validSourceIds.Contains(sourceId));
    }

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

    private static string Truncate(string? value, int maxLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    internal sealed record ModelSynthesisResponse(
        string Thesis,
        string Signal,
        int Confidence,
        IReadOnlyList<string> Insights,
        ModelThesisDetails ThesisDetails,
        IReadOnlyList<ModelInsightDetails> InsightDetails,
        ModelSignalDetails SignalDetails,
        ModelConfidenceDetails ConfidenceDetails);

    internal sealed record ModelThesisDetails(string Summary, IReadOnlyList<string> SourceIds);

    internal sealed record ModelInsightDetails(string Insight, IReadOnlyList<string> SourceIds);

    internal sealed record ModelSignalDetails(string Rationale, IReadOnlyList<string> SourceIds);

    internal sealed record ModelConfidenceDetails(string Rationale, IReadOnlyList<string> SourceIds);
}
