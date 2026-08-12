using System.Text.RegularExpressions;

namespace Api.Infrastructure;

/// <summary>
/// Serves the SPA's index.html for anything that isn't an API route or a static file.
///
/// The file is read once at startup rather than served straight off disk, because a
/// deployment under a sub-path needs its &lt;base href&gt; rewritten. Every asset and API
/// URL the frontend emits is relative to that tag, so this one substitution is what makes
/// the same build work at "/" and at "/calendar" without being rebuilt.
/// </summary>
public static partial class SpaShell
{
    public static void MapSpaFallback(this WebApplication app, string pathBase)
    {
        var indexPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");

        // No frontend build present, which is normal when running the API on its own and
        // letting the Vite dev server serve the SPA.
        if (!File.Exists(indexPath)) return;

        var html = File.ReadAllText(indexPath);

        if (!string.IsNullOrEmpty(pathBase))
        {
            var rewritten = BaseHrefPattern().Replace(html, $"<base href=\"{pathBase}/\"", 1);

            // Silently serving an unmodified shell would leave every asset pointing at the
            // wrong path, which looks like a broken deployment rather than a missing tag.
            if (rewritten == html)
                throw new InvalidOperationException(
                    $"ReverseProxy:PathBase is set but no <base href> tag was found in {indexPath}.");

            html = rewritten;
        }

        app.MapFallback((HttpContext http) =>
        {
            // The shell names the hashed asset files, so a cached copy outlives the assets
            // it points at. The assets themselves stay cacheable; only this must not be.
            http.Response.Headers.CacheControl = "no-store";
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }

    [GeneratedRegex("<base\\s+href=\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex BaseHrefPattern();
}
