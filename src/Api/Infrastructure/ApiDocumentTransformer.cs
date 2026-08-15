using System.Reflection;

using Api.Configuration;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Api.Infrastructure;

/// <summary>Fills in what the generated document can't work out for itself: what this
/// deployment calls itself, and that every route but a couple of them wants a bearer
/// token. Without the security scheme the reference UI has nowhere to put a token, which
/// makes it a document you can read rather than one you can use.</summary>
public sealed class ApiDocumentTransformer(
    IOptions<BrandingOptions> branding,
    IOptions<AuthOptions> auth) : IOpenApiDocumentTransformer
{
    private const string BearerScheme = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info.Title = $"{branding.Value.AppName} API";
        document.Info.Version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "1.0.0";

        document.Info.Description =
            "Absences, people, and bank holidays. Everything here needs a bearer token from "
            + $"the deployment's identity provider ({Authority()}), except /api/config/bootstrap, "
            + "which is what tells the frontend how to get one.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "An access token issued for the API's audience.",
        };

        // Applied to the whole document rather than per route: the fallback policy already
        // means authenticated-by-default, and the handful of anonymous routes are cheaper
        // to explain in the description above than to carve out here.
        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
            },
        ];

        return Task.CompletedTask;
    }

    private string Authority() =>
        auth.Value.Provider == AuthOptions.ProviderNone
            ? "bypassed in this environment"
            : auth.Value.Authority;
}
