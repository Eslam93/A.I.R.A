using Aira.Application.Models;

namespace Aira.Application.Abstractions;

public interface IIdentifierValidator
{
    IdentifierValidationResult Validate(string? identifier);
}
