using Proofsheet.Application.Models;

namespace Proofsheet.Application.Abstractions;

public interface IIdentifierValidator
{
    IdentifierValidationResult Validate(string? identifier);
}
