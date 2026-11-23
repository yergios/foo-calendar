using System.ComponentModel.DataAnnotations;

using Api.Dtos;

using Shouldly;

using UnitTests.TestSupport;

namespace UnitTests.Dtos;

/// <summary>The region code pattern, both where code checks it by hand and where a request
/// attribute does, against codes built to be valid and mutations of them that are not.
/// </summary>
public sealed class ValidationPatternsPropertyTests
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LettersAndDigits = Letters + "0123456789";

    private static readonly RegularExpressionAttribute Attribute = new(ValidationPatterns.RegionCode);

    private static bool Accepted(string code) =>
        ValidationPatterns.RegionCodeRegex().IsMatch(code) && Attribute.IsValid(code);

    private static bool Rejected(string code) =>
        !ValidationPatterns.RegionCodeRegex().IsMatch(code) && !Attribute.IsValid(code);

    private static string ValidCode(Gen gen) =>
        gen.Text(Letters, 2, 2) + (gen.Chance(0.5) ? "-" + gen.Text(LettersAndDigits, 1, 3) : "");

    public sealed record MutationCase(string Valid, string Mutated, string Mutation);

    private static MutationCase Mutate(Gen gen, string code)
    {
        switch (gen.Between(0, 4))
        {
            case 0:
                // A letter, never a digit, which has no lower case.
                var letters = Enumerable.Range(0, code.Length).Where(i => char.IsAsciiLetterUpper(code[i])).ToList();
                var at = gen.Pick(letters);
                return new(code, code[..at] + char.ToLowerInvariant(code[at]) + code[(at + 1)..], "lower case");
            case 1:
                var space = gen.Pick([" ", "\t", " "]);
                var where = gen.Between(0, code.Length);
                return new(code, code[..where] + space + code[where..], "whitespace");
            case 2:
                return new(code, code + gen.Pick(["\n", "\r\n"]), "trailing newline");
            case 3:
                // A country code alone takes one segment validly, so it gets two.
                var extra = code.Length == 2
                    ? $"-{gen.Text(LettersAndDigits, 1, 3)}-{gen.Text(LettersAndDigits, 1, 3)}"
                    : $"-{gen.Text(LettersAndDigits, 1, 3)}";
                return new(code, code + extra, "extra segment");
            default:
                var country = code[..2];
                return new(code, $"{country}-{gen.Text(LettersAndDigits, 4, 6)}", "subdivision too long");
        }
    }

    [Fact]
    [Trait("Spec", "REG-1")]
    public void TheRegionPattern_AcceptsEveryValidCode() => Property.Check(
        500,
        ValidCode,
        code => Accepted(code).ShouldBeTrue());

    [Fact]
    [Trait("Spec", "REG-1")]
    public void TheRegionPattern_RejectsEveryMutationOfAValidCode() => Property.Check(
        1000,
        gen => Mutate(gen, ValidCode(gen)),
        c => Rejected(c.Mutated).ShouldBeTrue(c.Mutation));
}
