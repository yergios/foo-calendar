namespace Api.Configuration;

/// <summary>How the app is exposed when it isn't reached directly. Both settings are off
/// by default, so a deployment that owns its own hostname needs none of this.</summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>Sub-path the app is served under, e.g. "/calendar" for
    /// https://intra.example.com/calendar. Empty means it owns the root of its host.
    /// Set this whether or not the proxy strips the prefix before forwarding: it is what
    /// tells the browser to ask for assets and API routes under the sub-path.</summary>
    public string PathBase { get; init; } = "";

    /// <summary>Read X-Forwarded-For/Proto/Host, so the scheme, client IP and host the
    /// original caller used survive the hop. Off by default: with nothing in front of the
    /// app, a client could set those headers itself and forge its own address.</summary>
    public bool TrustForwardedHeaders { get; init; }

    /// <summary>Addresses of the proxies to accept forwarded headers from. Empty accepts
    /// them from anywhere, which is the usual container setup, where the app is only
    /// reachable through the proxy in the first place.</summary>
    public List<string> KnownProxies { get; init; } = [];

    /// <summary>Same, as CIDR ranges, e.g. "10.0.0.0/8".</summary>
    public List<string> KnownNetworks { get; init; } = [];
}
