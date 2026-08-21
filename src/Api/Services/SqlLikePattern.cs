namespace Api.Services;

/// <summary>Builds a SQL LIKE pattern for a "contains" search, escaping the characters LIKE
/// treats specially - % and _, plus the escape character itself - so a literal one in the
/// search term (e.g. someone searching for "j_doe") can't silently act as a wildcard and
/// match names it has no business matching.</summary>
public static class SqlLikePattern
{
    /// <summary>Passed alongside <see cref="Contains"/> to EF.Functions.Like's three-
    /// argument overload, which is what actually makes it treat this as the escape
    /// character rather than a literal one.</summary>
    public const string EscapeChar = "\\";

    public static string Contains(string term) => $"%{Escape(term)}%";

    private static string Escape(string term) =>
        term.Replace(EscapeChar, EscapeChar + EscapeChar).Replace("%", EscapeChar + "%").Replace("_", EscapeChar + "_");
}
