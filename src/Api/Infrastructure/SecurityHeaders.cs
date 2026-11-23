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
    /// is the provider's origin, or null when there is none (the dev identity).</summary>
    public static string ShellPolicy(string inlineScript, string? identityProvider)
    {
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(inlineScript)));
        var provider = identityProvider is null ? "" : $" {identityProvider}";

        return string.Join("; ",
            "default-src 'self'",
            // Scripts are never relaxed: the bundle comes from the app's own origin, and the
            // one inline script is allowed by its hash alone.
            $"script-src 'self' 'sha256-{hash}'",
            "style-src 'self'",
            // The favicon and its mask-icon variant are data: URLs in index.html.
            "img-src 'self' data:",
            // The OIDC library fetches the provider's discovery document and token
            // endpoint, and renews silently in a hidden frame on the provider's origin.
            $"connect-src 'self'{provider}",
            $"frame-src 'self'{provider}",
            "object-src 'none'",
            "base-uri 'self'",
            $"form-action 'self'{provider}",
            "frame-ancestors 'none'");
    }

    /// <summary>The API reference's policy, for Scalar's page. Its configuration is an
    /// inline module script built per request, which <paramref name="nonce"/> allows; its
    /// bundle is served by the app. Its fonts, telemetry and agent, which would reach
    /// Scalar's own servers, are switched off in Program.cs, so everything else stays on
    /// the app's origin. Two relaxations, on this page only and never on the shell:
    ///
    /// - 'unsafe-eval': the bundle's schema validator (Zod) probes for Function() on load
    ///   and compiles its validators with it. It works without, but the probe is a
    ///   violation on every load, which would leave the end-to-end check unable to tell a
    ///   new one from it.
    /// - inline styles: Scalar styles itself with style elements and attributes throughout.
    /// </summary>
    public static string ApiReferencePolicy(string nonce) =>
        string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}' 'unsafe-eval'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self' data:",
            "connect-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
}
