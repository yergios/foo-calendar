using System.Globalization;
using System.Text;

using Api.Configuration;

using Microsoft.Extensions.Options;

namespace Api.Endpoints;

/// <summary>Endpoints defined by RFC 8615's well-known-URI convention rather than by this
/// app's own API surface.</summary>
public static class WellKnownEndpoints
{
    public static void MapWellKnownEndpoints(this WebApplication app)
    {
        app.MapGet("/.well-known/security.txt", GetSecurityTxt)
            .AllowAnonymous()
            .WithTags("WellKnown");
    }

    /// <summary>RFC 9116: where a security researcher who found a vulnerability on this
    /// deployment should report it. Computed per request rather than cached: Expires is a
    /// rolling one-year window, and nothing else here changes without a redeploy anyway.</summary>
    private static IResult GetSecurityTxt(IOptions<TeamOptions> team)
    {
        var contact = string.IsNullOrWhiteSpace(team.Value.SecurityContact)
            ? team.Value.SupportUrl
            : team.Value.SecurityContact;

        // Nothing configured to report a vulnerability to: publishing a security.txt with
        // no Contact field would be worse than not serving one at all.
        if (string.IsNullOrWhiteSpace(contact))
            return Results.NotFound();

        var expires = DateTimeOffset.UtcNow.AddYears(1)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var body = $"Contact: {contact}\nExpires: {expires}\nPreferred-Languages: en\n";

        return Results.Text(body, "text/plain", Encoding.UTF8);
    }
}
