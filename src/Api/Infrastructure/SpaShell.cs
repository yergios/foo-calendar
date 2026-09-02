using System.Text.Json;
using System.Text.RegularExpressions;

using Api.Configuration;
using Api.Endpoints;

using Microsoft.Extensions.Options;

namespace Api.Infrastructure;

/// <summary>
/// Serves the SPA's index.html for anything that isn't an API route or a static file.
///
/// The file is read once at startup rather than served straight off disk, because the
/// shell needs two substitutions this deployment's config decides:
///
/// - &lt;base href&gt;, so a deployment under a sub-path works without being rebuilt. Every
///   asset and API URL the frontend emits is relative to that tag.
/// - the bootstrap config, inlined as window.__bootstrap. The SPA cannot mount without
///   knowing how to sign in, so fetching it would put a serial round trip in front of
///   first paint; embedding it in the shell that was going to be downloaded anyway costs
///   nothing. The shell is already no-store, which is the caching policy that payload
///   needs anyway - a browser holding a stale copy would keep pointing at a retired IdP.
/// </summary>
public static partial class SpaShell
{
    /// <summary>Web defaults, i.e. camelCase, matching what the SPA reads off
    /// /api/config/bootstrap. The encoder is deliberately left at the default: it escapes
    /// '&lt;', '&gt;' and '&amp;' to their \u003c-style forms, which is the only reason it
    /// is safe to drop this JSON inside a &lt;script&gt; element at all. Do not "tidy" this
    /// into UnsafeRelaxedJsonEscaping - a config value containing a closing script tag
    /// would then end the element early and turn the rest of the payload into markup.</summary>
    private static readonly JsonSerializerOptions ShellJson = new(JsonSerializerDefaults.Web);

    public static void MapSpaFallback(this WebApplication app, string pathBase)
    {
        var indexPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");

        // No frontend build present, which is normal when running the API on its own and
        // letting the Vite dev server serve the SPA. That SPA has no shell of ours to read
        // window.__bootstrap out of, which is what the fetch fallback in the frontend's
        // bootstrapConfig.ts is for.
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

        // Resolved once here rather than per request: the auth options are fixed until a
        // redeploy, which is the same lifetime as the shell string itself.
        var team = app.Services.GetRequiredService<IOptions<TeamOptions>>().Value;
        var auth = app.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        html = InlineBootstrapConfig(html, team, auth, app.Environment, indexPath);

        app.MapFallback((HttpContext http) =>
        {
            // The shell names the hashed asset files, so a cached copy outlives the assets
            // it points at. The assets themselves stay cacheable; only this must not be.
            http.Response.Headers.CacheControl = "no-store";
            return Results.Content(html, "text/html; charset=utf-8");
        })
        // Opting out of the fallback authorization policy: this is the page that has to
        // load *before* anyone can sign in. It is the same public index.html the static
        // file middleware already serves, and it carries no user data: every field in the
        // inlined payload is something the browser is handed anyway to talk to the IdP.
        .AllowAnonymous();
    }

    private static string InlineBootstrapConfig(
        string html, TeamOptions team, AuthOptions auth, IHostEnvironment environment, string indexPath)
    {
        var json = JsonSerializer.Serialize(
            ConfigEndpoints.Build(team, auth, environment), ShellJson);
        var tag = $"<script>window.__bootstrap={json}</script>";

        var head = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        // Same reasoning as the base-href check above: the shell is our own build output,
        // so a missing </head> is a broken build, not a deployment someone can configure
        // their way out of.
        if (head < 0)
            throw new InvalidOperationException(
                $"No </head> tag was found in {indexPath}, so the bootstrap config could not be inlined.");

        return html[..head] + tag + html[head..];
    }

    [GeneratedRegex("<base\\s+href=\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex BaseHrefPattern();
}
