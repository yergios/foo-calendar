namespace Api.Configuration;

/// <summary>The OpenAPI document and the Scalar reference UI served from it.</summary>
public sealed class ApiDocsOptions
{
    public const string SectionName = "ApiDocs";

    /// <summary>Whether the document and the reference UI are served at all. On in
    /// Development, off everywhere else: both routes are anonymous, since a browser
    /// opening them carries no bearer token, so turning this on publishes the shape of the
    /// API (paths, parameters, schemas, never data) to anyone who can reach the app.
    /// Worth it on an internal deployment whose users script against it; think about it
    /// first on one that faces further than that.</summary>
    public bool Enabled { get; init; }

    /// <summary>Where the reference UI is served. The document itself sits at
    /// /openapi/v1.json, which is what Microsoft.AspNetCore.OpenApi decides.</summary>
    public string RoutePrefix { get; init; } = "/scalar";
}
