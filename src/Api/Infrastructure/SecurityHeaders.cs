using System.Security.Cryptography;
using System.Text;

using Scalar.AspNetCore;

namespace Api.Infrastructure;

/// <summary>
/// The response headers that tell the browser what a page may do (docs/decisions/0015).
/// Both Content-Security-Policies live here, side by side, so the shell's and the API
/// reference's can be compared at a glance: the reference is a third-party UI and gets a
/// policy of its own rather than a reason to loosen the shell's.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>Nothing about the page, path or query, goes to another origin, and the app
    /// itself reads no Referer. Same-origin requests lose it too, which costs nothing.
    /// </summary>
    public const string ReferrerPolicy = "no-referrer";

    /// <summary>Adds X-Content-Type-Options and Referrer-Policy to every response, and the
    /// API reference's policy to its page. First in the pipeline, and applied as the
    /// response starts rather than now: the exception handler clears the headers of a
    /// response it takes over, and the 500 it writes needs them as much as any other.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var http = (HttpContext)state;
                var headers = http.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = ReferrerPolicy;

                // Scalar sets this only on its page, with the nonce its script tags carry
                // on this one response (WithNonce in Program.cs).
                if (http.Items[ScalarOptions.NonceHttpContextItemKey] is string nonce)
                    headers.ContentSecurityPolicy = ApiReferencePolicy(nonce);

                return Task.CompletedTask;
            }, context);

            return next(context);
        });

    /// <summary>The SPA shell's policy. <paramref name="inlineScript"/> is the text of the
    /// one inline script element, exactly as it is served: its hash is computed from the
    /// same string, so the shell and its policy cannot disagree. <paramref name="identityProvider"/>
    /// is the origin of Auth:Authority, or null when there is none (the dev identity), and
    /// <paramref name="extraOrigins"/> those of Auth:ExtraOrigins, both as
    /// <see cref="OriginOf"/> gives them.</summary>
    public static string ShellPolicy(
        string inlineScript, string? identityProvider, IReadOnlyCollection<string> extraOrigins)
    {
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(inlineScript)));
        var provider = identityProvider is null ? "" : $" {identityProvider}";
        var connections = string.Concat(
            extraOrigins.Where(o => o != identityProvider).Distinct().Select(o => $" {o}"));

        return string.Join("; ",
            "default-src 'self'",
            // Scripts are never relaxed: the bundle comes from the app's own origin, and the
            // one inline script is allowed by its hash alone.
            $"script-src 'self' 'sha256-{hash}'",
            "style-src 'self'",
            // The favicon and its mask-icon variant are data: URLs in index.html.
            "img-src 'self' data:",
            // The OIDC library fetches the provider's discovery document and token endpoint,
            // and renews with a refresh token at the same endpoint. A provider that serves
            // that endpoint from another host names it in Auth:ExtraOrigins. Sign-in and
            // sign-out themselves are navigations, which this does not govern.
            $"connect-src 'self'{provider}{connections}",
            // Nothing frames the provider: renewing in a hidden frame cannot work under
            // frame-ancestors 'none', which the page it would load back into carries, and
            // renewal goes through refresh tokens instead (docs/decisions/0017).
            "frame-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            $"form-action 'self'{provider}",
            "frame-ancestors 'none'");
    }

    /// <summary>A URL's origin as a Content-Security-Policy source: scheme, host and a port
    /// other than the scheme's default. The host is in its ASCII (punycode) form, since a
    /// response header carries nothing else, and an IPv6 address keeps its brackets.
    /// </summary>
    public static string OriginOf(Uri uri)
    {
        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.IdnHost}]" : uri.IdnHost;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{host}" : $"{uri.Scheme}://{host}:{uri.Port}";
    }

    /// <summary>Whether <paramref name="value"/> is an absolute origin and nothing more: an
    /// https URL (or http, when <paramref name="allowHttp"/>) with a host and an optional
    /// port, and no user info, path, query or fragment. A single trailing slash is allowed,
    /// since that is how an origin is often copied.</summary>
    public static bool IsOrigin(string value, bool allowHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp)) return false;

        // Read off the text rather than the parsed URI, which would have normalized a path
        // such as "/." away: after "://" comes the host and port, and nothing else.
        var rest = value.Trim();
        rest = rest[(rest.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (rest.EndsWith('/')) rest = rest[..^1];
        return rest.Length > 0 && rest.IndexOfAny(['/', '\\', '?', '#', '@']) < 0;
    }

    /// <summary>The API reference's policy, for Scalar's page. Its configuration is an
    /// inline module script built per request, which <paramref name="nonce"/> allows; its
    /// bundle is served by the app. Its fonts, telemetry and agent, which would reach
    /// Scalar's own servers, are switched off in Program.cs, so everything else stays on
    /// the app's origin.
    ///
    /// No 'unsafe-eval': the page shares the app's origin and storage, so it gets no more
    /// script than the shell does. The bundle's schema validator (Zod) probes whether
    /// Function() works on every load and falls back to running without it, so that probe
    /// is reported as a violation each time; the end-to-end check expects exactly that one,
    /// matched by directive, blocked URI and file (docs/decisions/0017).
    ///
    /// One relaxation, on this page only and never on the shell: inline styles, since
    /// Scalar styles itself with style elements and attributes throughout.
    /// </summary>
    public static string ApiReferencePolicy(string nonce) =>
        string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self' data:",
            "connect-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
}
