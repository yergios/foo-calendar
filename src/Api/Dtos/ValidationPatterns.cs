using System.Text.RegularExpressions;

namespace Api.Dtos;

public static partial class ValidationPatterns
{
    /// <summary>ISO 3166-2 region/country code, e.g. "ES" or "ES-MD". Ends in \z, not $:
    /// $ also matches before a final newline, which would let "ES\n" through anywhere the
    /// pattern is used without the whole-string check RegularExpressionAttribute adds.
    /// </summary>
    public const string RegionCode = @"^[A-Z]{2}(-[A-Z0-9]{1,3})?\z";

    /// <summary><see cref="RegionCode"/> compiled at build time, for code that checks a value
    /// by hand rather than through a [RegularExpression] attribute. The pattern is anchored
    /// and cannot backtrack, so the timeout never fires in practice; it is there so a future
    /// edit to the pattern can't turn a request parameter into an unbounded match.</summary>
    [GeneratedRegex(RegionCode, RegexOptions.None, matchTimeoutMilliseconds: 100)]
    public static partial Regex RegionCodeRegex();
}
