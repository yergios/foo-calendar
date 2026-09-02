namespace Api.Configuration;

/// <summary>The product's own identity. Deliberately a constant and not configuration:
/// the name and mark are the licensor's, and the license does not grant the right to
/// rebrand the app as something else (see TRADEMARK.md). A deployment names *itself* with
/// Team:OrgName, which is a different thing: whose calendar this is, not what this
/// software is.</summary>
public static class ProductInfo
{
    public const string Name = "Foo Calendar";

    /// <summary>Copyright holder, as it appears in the app's footer and in NOTICE.txt.</summary>
    public const string Copyright = "Sergio Y. Lee";
}
