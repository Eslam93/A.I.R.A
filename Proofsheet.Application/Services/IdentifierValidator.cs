using System.Text.RegularExpressions;
using Proofsheet.Application.Abstractions;
using Proofsheet.Application.Models;

namespace Proofsheet.Application.Services;

public sealed partial class IdentifierValidator : IIdentifierValidator
{
    public IdentifierValidationResult Validate(string? identifier)
    {
        if (identifier is null)
        {
            return IdentifierValidationResult.Failure(InvalidRequest("The identifier field is required."));
        }

        var normalized = identifier.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            return IdentifierValidationResult.Failure(InvalidRequest("The identifier field must not be blank."));
        }

        if (normalized.Length > 16)
        {
            return IdentifierValidationResult.Failure(InvalidRequest("The identifier must be 16 characters or fewer."));
        }

        if (!IdentifierRegex().IsMatch(normalized))
        {
            return IdentifierValidationResult.Failure(InvalidRequest("The identifier contains unsupported characters."));
        }

        return IdentifierValidationResult.Success(normalized);
    }

    private static ProblemDescriptor InvalidRequest(string detail) =>
        new(
            400,
            "/problems/invalid-request",
            "Invalid request.",
            detail,
            "InvalidRequest");

    [GeneratedRegex("^[A-Z][A-Z0-9.\\-]{0,15}$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();
}
