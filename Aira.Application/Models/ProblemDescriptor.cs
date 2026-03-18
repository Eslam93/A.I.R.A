namespace Aira.Application.Models;

public sealed record ProblemDescriptor(
    int StatusCode,
    string Type,
    string Title,
    string Detail,
    string ErrorCode,
    Guid? JobId = null,
    string? Identifier = null,
    string? CorrelationId = null);
