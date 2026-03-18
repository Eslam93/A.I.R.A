using Aira.Domain.Enums;

namespace Aira.Application.Models;

public sealed class JobExecutionContext
{
    private readonly HashSet<string> _consumedTransientFailureKeys = new(StringComparer.OrdinalIgnoreCase);

    public required Guid JobId { get; init; }

    public required string Identifier { get; init; }

    public required string CorrelationId { get; init; }

    public required ResearchExecutionMode ExecutionMode { get; init; }

    public required ResolvedTickerInfo ResolvedTicker { get; init; }

    public bool ConsumeTransientFailure(string key) => _consumedTransientFailureKeys.Add(key);
}
