using Aira.Application.Services;

namespace Aira.Tests.Unit.Validation;

public sealed class IdentifierValidatorTests
{
    private readonly IdentifierValidator _validator = new();

    [Fact]
    public void Validate_Normalizes_And_Accepts_Supported_Identifier()
    {
        var result = _validator.Validate("  aapl ");

        Assert.True(result.IsValid);
        Assert.Equal("AAPL", result.NormalizedIdentifier);
        Assert.Null(result.Problem);
    }

    [Fact]
    public void Validate_Accepts_Syntax_Valid_Unknown_Symbol()
    {
        var result = _validator.Validate("META");

        Assert.True(result.IsValid);
        Assert.Equal("META", result.NormalizedIdentifier);
        Assert.Null(result.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad symbol!")]
    public void Validate_Returns_InvalidRequest_For_Bad_Input(string identifier)
    {
        var result = _validator.Validate(identifier);

        Assert.False(result.IsValid);
        Assert.Equal(400, result.Problem!.StatusCode);
        Assert.Equal("InvalidRequest", result.Problem.ErrorCode);
    }
}
