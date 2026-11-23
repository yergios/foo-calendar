using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

using Api.Configuration;
using Api.Endpoints;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Api.Infrastructure;

/// <summary>The SPA shell's HTML and the Content-Security-Policy it is served with, both
/// prepared once by <see cref="SpaShell.Prepare"/>.</summary>
public sealed record PreparedShell(string Html, string ContentSecurityPolicy);

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
///
/// Its Content-Security-Policy is built here too, from the inlined script itself
/// (docs/decisions/0015).
/// </summary>
public static partial class SpaShell
{
    /// <summary>The shell to serve, read and rewritten for this deployment, or null when
    /// there is no frontend build. Separate from <see cref="MapSpaFallback"/> so Program.cs
    /// can run it, and fail on a broken shell, before the database is migrated.</summary>
    public static PreparedShell? Prepare(WebApplication app, string pathBase)
    {
        var indexPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");

        // No frontend build present, which is normal when running the API on its own and
        // letting the Vite dev server serve the SPA. That SPA has no shell of ours to read
        // window.__bootstrap out of, which is what the fetch fallback in the frontend's
        // bootstrapConfig.ts is for.
        if (!File.Exists(indexPath)) return null;

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
        // The very options the API serializes its own responses with, rather than a second
        // set that happens to match today: ConfigEndpoints.Build exists so the shell can't
        // hold a drifting copy of the payload, and the serializer settings are part of it.
        // The first enum added to the payload would otherwise reach the browser as a number
        // here and a string from the endpoint - on the served deployment only, never under
        // the Vite dev server that the fetch path serves.
        var json = app.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;
        var script = BootstrapScript(team, auth, json);

        // The validation that ran before this has made sure Authority is an absolute URL
        // whenever there is a provider, and every extra origin an origin. The dev identity
        // has no provider to talk to, so neither applies to it.
        var identityProvider = auth.Provider == AuthOptions.ProviderNone
            ? null
            : SecurityHeaders.OriginOf(new Uri(auth.Authority));
        IReadOnlyList<string> extraOrigins = identityProvider is null
            ? []
            : [.. auth.ExtraOrigins.Select(o => SecurityHeaders.OriginOf(new Uri(o)))];

        return new(
            InlineScript(html, script, indexPath),
            SecurityHeaders.ShellPolicy(script, identityProvider, extraOrigins));
    }

    /// <summary>Serves <paramref name="shell"/> from <see cref="Prepare"/>, or nothing when
    /// it is null.</summary>
    public static void MapSpaFallback(this WebApplication app, PreparedShell? shell)
    {
        if (shell is null) return;

        // GET and HEAD only, the requests a browser navigates with. Routing answers 405 or
        // 415 only when no route at all is left for a request's method or content type, and
        // a fallback taking every method always was: a POST to an API route with the wrong
        // content type came back as a 200 page.
        HttpMethodMetadata navigation = new([HttpMethods.Get, HttpMethods.Head]);

        app.MapFallback((HttpContext http) =>
        {
            // The shell names the hashed asset files, so a cached copy outlives the assets
            // it points at. The assets themselves stay cacheable; only this must not be.
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.ContentSecurityPolicy = shell.ContentSecurityPolicy;
            return Results.Content(shell.Html, "text/html; charset=utf-8");
        })
        .WithMetadata(navigation)
        // Opting out of the fallback authorization policy: this is the page that has to
        // load *before* anyone can sign in. It is the same public index.html the static
        // file middleware already serves, and it carries no user data: every field in the
        // inlined payload is something the browser is handed anyway to talk to the IdP.
        .AllowAnonymous();

        // An API path no route takes is a failed API call, not a page. More specific than the
        // shell's pattern, so it takes every such GET first, and members only, like the
        // rest of the API: whoever is not signed in gets the same 401 a real route gives.
        app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound))
            .WithMetadata(navigation);
    }

    /// <summary>The bootstrap configuration as the text of a script element.</summary>
    private static string BootstrapScript(TeamOptions team, AuthOptions auth, JsonSerializerOptions options)
    {
        // The API's options, but never their encoder: ASP.NET Core sets that to
        // UnsafeRelaxedJsonEscaping, which is fine for a response body and unsafe here. The
        // default encoder escapes '<', '>' and '&' to their \u003C-style forms, which is the
        // only reason it is safe to drop this JSON inside a <script> element at all: with
        // the relaxed one, a config value containing a closing script tag ends the element
        // early and turns the rest of the payload into markup.
#pragma warning disable CA1869 // Runs once, at startup, so there is nothing to cache the options for.
        var escaping = new JsonSerializerOptions(options) { Encoder = JavaScriptEncoder.Default };
#pragma warning restore CA1869
        var json = JsonSerializer.Serialize(ConfigEndpoints.Build(team, auth), escaping);
        return $"window.__bootstrap={json}";
    }

    private static string InlineScript(string html, string script, string indexPath)
    {
        var tag = $"<script>{script}</script>";
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

/// <summary>
/// The web root as the static file middleware sees it: every file but index.html.
///
/// Served off disk, index.html is the shell before <see cref="SpaShell.Prepare"/> has
/// touched it: no Content-Security-Policy, no rewritten &lt;base href&gt;, no bootstrap
/// config, and cacheable. The SPA boots from it all the same, so any script injected into
/// the page would run unchecked. Hidden here, a request for it falls through to the
/// fallback like any other path, which answers with the prepared shell and its policy.
///
/// Matched by the file a path resolves to rather than by the path itself, so every
/// spelling that reaches the same file is covered: "/index.html", "//index.html", and
/// whatever a case-insensitive file system also maps onto it.
/// </summary>
public sealed class WebRootWithoutShell(string webRoot) : IFileProvider, IDisposable
{
    private readonly PhysicalFileProvider files = new(webRoot);
    private readonly string shellPath = Path.GetFullPath(Path.Combine(webRoot, "index.html"));

    public IFileInfo GetFileInfo(string subpath)
    {
        var file = files.GetFileInfo(subpath);

        // Ignoring case costs nothing on a case-sensitive file system, where no other file
        // in the build differs from index.html by case alone.
        return file.PhysicalPath is { } path
            && string.Equals(Path.GetFullPath(path), shellPath, StringComparison.OrdinalIgnoreCase)
                ? new NotFoundFileInfo(subpath)
                : file;
    }

    // The static file middleware never lists directories; this is here to satisfy the
    // interface, and lists what the disk has.
    public IDirectoryContents GetDirectoryContents(string subpath) => files.GetDirectoryContents(subpath);

    public IChangeToken Watch(string filter) => files.Watch(filter);

    public void Dispose() => files.Dispose();
}
