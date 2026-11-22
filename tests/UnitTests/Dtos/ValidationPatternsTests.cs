using System.ComponentModel.DataAnnotations;

using Api.Dtos;

using Shouldly;

namespace UnitTests.Dtos;

public sealed class ValidationPatternsTests
{
    [Theory]
    [Trait("Spec", "REG-1")]
    [InlineData("ES")]
    [InlineData("ES-MD")]
    [InlineData("GB-ENG")]
    [InlineData("FR-75")]
    public void RegionCodeRegex_AcceptsACountryOrSubdivision(string code)
    {
        ValidationPatterns.RegionCodeRegex().IsMatch(code).ShouldBeTrue();
    }

    // "ES\n" is the case the pattern's \z is there for: $ would match before the newline.
    [Theory]
    [Trait("Spec", "REG-1")]
    [InlineData("ES\n")]
    [InlineData(" ES")]
    [InlineData("ES ")]
    [InlineData("es")]
    [InlineData("ESP")]
    [InlineData("ES-")]
    [InlineData("ES-MADR")]
    [InlineData("")]
    public void RegionCodeRegex_RejectsAnythingElse(string code)
    {
        ValidationPatterns.RegionCodeRegex().IsMatch(code).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "PPL-3")]
    [Trait("Spec", "REG-1")]
    public void CreatePersonRequest_WithATrailingNewlineInTheRegion_IsInvalid()
    {
        var request = new CreatePersonRequest("ana", "Ana", "Lopez", "Ana", "ES\n");
        var errors = new List<ValidationResult>();

        Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true)
            .ShouldBeFalse();
    }
}
