using Api.Configuration;
using Api.Data;

using IntegrationTests.TestSupport;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace IntegrationTests;

public sealed class StartupTests
{
    private static Exception StartupFailure(TestApp app) => Should.Throw<Exception>(() => app.Services);

    [Fact]
    [Trait("Spec", "CFG-2")]
    [Trait("Spec", "CFG-1")]
    public void InvalidSetting_StopsStartupNamingIt_WithTheDatabaseUntouched()
    {
        using var app = new TestApp(("Team:HomeRegion", "Zurich"));
        File.WriteAllBytes(app.DatabasePath, []);

        StartupFailure(app).Message.ShouldContain("Team:HomeRegion");

        app.TableNames().ShouldBeEmpty();
    }

    // Each of these used to pass validation and fail only once the database had been
    // migrated: the addresses when the forwarded headers middleware was built, the URL when
    // the first sync created its HTTP client.
    [Theory]
    [Trait("Spec", "CFG-1")]
    [InlineData("ReverseProxy:KnownProxies:0", "not-an-ip", "ReverseProxy:KnownProxies")]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.0.0.0/33", "ReverseProxy:KnownNetworks")]
    [InlineData("ReverseProxy:KnownNetworks:0", "10.0.0.1", "ReverseProxy:KnownNetworks")]
    [InlineData("Holidays:Nager:BaseUrl", "date.nager.at/api/v3/", "Holidays:Nager:BaseUrl")]
    [InlineData("Holidays:Nager:BaseUrl", "ftp://date.nager.at/api/v3/", "Holidays:Nager:BaseUrl")]
    public void MalformedSetting_StopsStartupNamingIt_WithTheDatabaseUntouched(
        string key, string value, string named)
    {
        using var app = new TestApp(
            ("ReverseProxy:TrustForwardedHeaders", "true"),
            ("Holidays:Source", "Nager"),
            (key, value));
        File.WriteAllBytes(app.DatabasePath, []);

        StartupFailure(app).Message.ShouldContain(named);

        app.TableNames().ShouldBeEmpty();
    }

    [Fact]
    [Trait("Spec", "CFG-1")]
    public void WellFormedProxyAddresses_Start()
    {
        using var app = new TestApp(
            ("ReverseProxy:TrustForwardedHeaders", "true"),
            ("ReverseProxy:KnownProxies:0", "10.0.0.5"),
            ("ReverseProxy:KnownProxies:1", "fd00::5"),
            ("ReverseProxy:KnownNetworks:0", "10.0.0.0/8"));

        Should.NotThrow(() => app.Services);
    }

    [Fact]
    [Trait("Spec", "CFG-1")]
    public void PathBaseWithAShellLackingBaseHref_StopsStartupNamingIt_WithTheDatabaseUntouched()
    {
        using var app = new TestApp(("ReverseProxy:PathBase", "/cal"))
        {
            Shell = "<!doctype html><html><head><title>x</title></head><body></body></html>",
        };
        File.WriteAllBytes(app.DatabasePath, []);

        StartupFailure(app).Message.ShouldContain("ReverseProxy:PathBase");

        app.TableNames().ShouldBeEmpty();
    }

    // Not a setting anyone can fix, but a broken build, which should still stop the app
    // before it migrates a database it will never serve.
    [Fact]
    [Trait("Spec", "CFG-1")]
    public void ShellLackingHead_StopsStartup_WithTheDatabaseUntouched()
    {
        using var app = new TestApp { Shell = "<!doctype html><html><body></body></html>" };
        File.WriteAllBytes(app.DatabasePath, []);

        StartupFailure(app).Message.ShouldContain("</head>");

        app.TableNames().ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "HOL-1")]
    [InlineData("Holidays:Source", "Calendarific", "Holidays:Source")]
    [InlineData("Holidays:Source", "File", "Holidays:File:Path")]
    public void InvalidHolidaySource_StopsStartupNamingTheSetting(string key, string value, string named)
    {
        using var app = new TestApp((key, value));

        StartupFailure(app).Message.ShouldContain(named);
    }

    [Theory]
    [Trait("Spec", "CFG-2")]
    [Trait("Spec", "CFG-1")]
    [InlineData("Team:HomeRegion")]
    [InlineData("Team:HomeTimeZone")]
    [InlineData("ConnectionStrings:FooCalendarDb")]
    public void RequiredSettingLeftBlank_StopsStartupNamingIt(string key)
    {
        using var app = new TestApp((key, ""));

        StartupFailure(app).Message.ShouldContain($"{key} must be configured");
    }

    [Fact]
    [Trait("Spec", "CFG-2")]
    public void HomeTimeZoneThatIsNotAnIanaId_StopsStartupNamingIt()
    {
        using var app = new TestApp(("Team:HomeTimeZone", "Central European Time"));

        StartupFailure(app).Message.ShouldContain("Team:HomeTimeZone must be a valid IANA timezone id");
    }

    private static readonly (string Key, string Value)[] IdentityProvider =
    [
        ("Auth:Authority", "https://idp.example.test/"),
        ("Auth:Audience", "foo-calendar"),
        ("Auth:ClientId", "foo-calendar-spa"),
    ];

    // Outside Development there is no dev bypass, so the identity provider settings, blank
    // in appsettings.json, have to come from the deployment.
    [Theory]
    [Trait("Spec", "CFG-2")]
    [InlineData("Auth:Authority")]
    [InlineData("Auth:Audience")]
    [InlineData("Auth:ClientId")]
    public void IdentityProviderSettingMissingOutsideDevelopment_StopsStartupNamingIt(string missing)
    {
        using var app = new TestApp([.. IdentityProvider.Where(s => s.Key != missing)])
        {
            Environment = "Production",
        };

        var message = StartupFailure(app).Message;

        message.ShouldContain($"{missing} must be configured");
        foreach (var (configured, _) in IdentityProvider.Where(s => s.Key != missing))
            message.ShouldNotContain(configured);
    }

    [Fact]
    [Trait("Spec", "CFG-2")]
    public void IdentityProviderConfiguredOutsideDevelopment_Starts()
    {
        using var app = new TestApp(IdentityProvider) { Environment = "Production" };

        Should.NotThrow(() => app.Services);
    }

    // Development included: the dev bypass is off here, so the app validates bearer tokens
    // the way a deployment does and the authority's scheme matters.
    private static TestApp WithAuthority(string environment, string authority) =>
        new([.. IdentityProvider.Where(s => s.Key != "Auth:Authority"),
            ("Auth:Provider", "Oidc"),
            ("Auth:Authority", authority)])
        {
            Environment = environment,
        };

    [Theory]
    [Trait("Spec", "CFG-2")]
    [InlineData("Production", "https://idp.example.test/")]
    [InlineData("Development", "https://idp.example.test/")]
    [InlineData("Development", "http://localhost:8080/realms/team")]
    public void AuthorityOverHttps_OrOverHttpInDevelopment_Starts(string environment, string authority)
    {
        using var app = WithAuthority(environment, authority);

        Should.NotThrow(() => app.Services);
    }

    [Fact]
    [Trait("Spec", "CFG-2")]
    public void AuthorityOverHttpOutsideDevelopment_StopsStartupNamingIt()
    {
        using var app = WithAuthority("Production", "http://idp.example.test/");

        StartupFailure(app).Message.ShouldContain("Auth:Authority must be an absolute https URL");
    }

    // An extra provider origin becomes a source in the shell's policy, so anything more or
    // less than an origin is refused before the database is touched.
    [Theory]
    [Trait("Spec", "CFG-1")]
    [Trait("Spec", "SEC-1")]
    [InlineData("Production", "http://tokens.example.test")]
    [InlineData("Production", "https://tokens.example.test/oauth2/token")]
    [InlineData("Production", "https://tokens.example.test/.")]
    [InlineData("Production", "https://tokens.example.test?x=1")]
    [InlineData("Production", "https://tokens.example.test#top")]
    [InlineData("Production", "https://user@tokens.example.test")]
    [InlineData("Production", "tokens.example.test")]
    [InlineData("Production", "/oauth2/token")]
    [InlineData("Development", "ftp://tokens.example.test")]
    [InlineData("Development", "http://localhost:9001/token")]
    public void AnExtraOriginThatIsNotAnHttpsOrigin_StopsStartupNamingIt_WithTheDatabaseUntouched(
        string environment, string origin)
    {
        using var app = new TestApp([.. IdentityProvider, ("Auth:Provider", "Oidc"), ("Auth:ExtraOrigins:0", origin)])
        {
            Environment = environment,
        };
        File.WriteAllBytes(app.DatabasePath, []);

        StartupFailure(app).Message.ShouldContain("Auth:ExtraOrigins must list absolute");

        app.TableNames().ShouldBeEmpty();
    }

    [Theory]
    [Trait("Spec", "CFG-1")]
    [Trait("Spec", "SEC-1")]
    [InlineData("Production", "https://example.auth.eu-west-1.amazoncognito.com")]
    [InlineData("Production", "https://tokens.example.test:8443/")]
    [InlineData("Production", "https://[2001:db8::1]:8443")]
    [InlineData("Development", "http://localhost:9001")]
    public void AnExtraOriginThatIsAnOrigin_Starts(string environment, string origin)
    {
        using var app = new TestApp([.. IdentityProvider, ("Auth:Provider", "Oidc"), ("Auth:ExtraOrigins:0", origin)])
        {
            Environment = environment,
        };

        Should.NotThrow(() => app.Services);
    }

    // Startup validation refuses http outside Development, and this is the second lock
    // behind it: the bearer handler itself refuses to fetch keys over http. Development
    // turns it off so the http authority it accepts can be reached.
    [Theory]
    [Trait("Spec", "CFG-2")]
    [InlineData("Production", "https://idp.example.test/", true)]
    [InlineData("Development", "http://localhost:8080/realms/team", false)]
    public void TheBearerHandler_RequiresHttpsMetadata_OutsideDevelopment(
        string environment, string authority, bool required)
    {
        using var app = WithAuthority(environment, authority);

        var options = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.RequireHttpsMetadata.ShouldBe(required);
    }

    [Theory]
    [Trait("Spec", "CFG-2")]
    [InlineData("Production", "/realms/team")]
    [InlineData("Production", "idp.example.test")]
    [InlineData("Development", "/realms/team")]
    public void RelativeAuthority_StopsStartupNamingIt(string environment, string authority)
    {
        using var app = WithAuthority(environment, authority);

        StartupFailure(app).Message.ShouldContain("Auth:Authority must be an absolute");
    }

    // Nothing ships for it, so this is a deployment that never set it.
    [Fact]
    [Trait("Spec", "CFG-2")]
    [Trait("Spec", "CFG-1")]
    public void SupportUrlMissingOutsideDevelopment_StopsStartupNamingIt_WithTheDatabaseUntouched()
    {
        using var app = new TestApp([.. IdentityProvider, ("Team:SupportUrl", "")])
        {
            Environment = "Production",
        };
        File.WriteAllBytes(app.DatabasePath, []);

        StartupFailure(app).Message.ShouldContain("Team:SupportUrl must be configured");

        app.TableNames().ShouldBeEmpty();
    }

    // What a deployment gets when it sets nothing: proves no contact ships in appsettings.json
    // for it to fall back on (0020).
    [Fact]
    [Trait("Spec", "CFG-2")]
    [Trait("Spec", "CFG-1")]
    public void NoSupportUrlShipsWithTheApp_SoAProductionStartWithoutOneStops()
    {
        using var app = new TestApp(IdentityProvider)
        {
            Environment = "Production",
            SupportUrl = null,
        };

        StartupFailure(app).Message.ShouldContain("Team:SupportUrl must be configured");
    }

    // "/support" because on Linux a rooted path parses as an absolute file URI, yet links to
    // a page of the app itself like any other relative value. Padding is refused rather than
    // trimmed (0020): Uri.TryCreate trims, but the notices and security.txt show the value as
    // written. A trailing newline is what a secret file leaves.
    [Theory]
    [Trait("Spec", "CFG-2")]
    [Trait("Spec", "CFG-1")]
    [InlineData("Production", "support")]
    [InlineData("Production", "/support")]
    [InlineData("Development", "support")]
    [InlineData("Production", " mailto:it@acme.example")]
    [InlineData("Production", "mailto:x\n")]
    [InlineData("Production", "\tmailto:x")]
    [InlineData("Development", " mailto:it@acme.example")]
    public void RelativeOrPaddedSupportUrl_StopsStartupNamingIt(string environment, string supportUrl)
    {
        using var app = new TestApp([.. IdentityProvider, ("Team:SupportUrl", supportUrl)])
        {
            Environment = environment,
        };

        StartupFailure(app).Message.ShouldContain("Team:SupportUrl must be an absolute URL");
    }

    // Whitespace alone is a value nobody meant to set, so the operator is told to set one.
    [Theory]
    [Trait("Spec", "CFG-2")]
    [Trait("Spec", "CFG-1")]
    [InlineData(" ")]
    [InlineData("\n")]
    public void WhitespaceSupportUrlOutsideDevelopment_StopsStartupAsNotConfigured(string supportUrl)
    {
        using var app = new TestApp([.. IdentityProvider, ("Team:SupportUrl", supportUrl)])
        {
            Environment = "Production",
        };

        StartupFailure(app).Message.ShouldContain("Team:SupportUrl must be configured");
    }

    // It would render an empty link where the contact should be (0020).
    [Theory]
    [Trait("Spec", "CFG-2")]
    [Trait("Spec", "CFG-1")]
    [InlineData("mailto:")]
    [InlineData("MAILTO:")]
    [InlineData("https:")]
    public void SchemeOnlySupportUrl_StopsStartupNamingIt(string supportUrl)
    {
        using var app = new TestApp([.. IdentityProvider, ("Team:SupportUrl", supportUrl)])
        {
            Environment = "Production",
        };

        StartupFailure(app).Message.ShouldContain("Team:SupportUrl must be an absolute URL");
    }

    [Theory]
    [Trait("Spec", "CFG-2")]
    [InlineData("mailto:it@acme.example")]
    [InlineData("MAILTO:it@acme.example?subject=Foo%20Calendar")]
    [InlineData("https://help.acme.example/calendar")]
    public void AbsoluteSupportUrlInAnyScheme_StartsOutsideDevelopment(string supportUrl)
    {
        using var app = new TestApp([.. IdentityProvider, ("Team:SupportUrl", supportUrl)])
        {
            Environment = "Production",
        };

        Should.NotThrow(() => app.Services);
    }

    // A local run starts with no configuration, as it does without an identity provider.
    [Fact]
    [Trait("Spec", "CFG-2")]
    public void SupportUrlMissingInDevelopment_Starts()
    {
        using var app = new TestApp(("Team:SupportUrl", ""));

        Should.NotThrow(() => app.Services);
    }

    [Fact]
    [Trait("Spec", "CFG-4")]
    public void ValidConfiguration_MigratesAnEmptyDatabase()
    {
        using var app = new TestApp();
        File.WriteAllBytes(app.DatabasePath, []);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Database.GetPendingMigrations().ShouldBeEmpty();
        app.TableNames().ShouldContain("Absences");
    }

    // Not only true: a value that isn't a bool at all was still meant as asking for it.
    [Theory]
    [Trait("Spec", "CFG-3")]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("yes")]
    [InlineData("1")]
    public void AbsenceApprovalsEnabled_StopsStartupSayingTheFeatureWasRemoved(string value)
    {
        using var app = new TestApp(("AbsenceApprovals:Enabled", value));

        var failure = StartupFailure(app);

        failure.Message.ShouldContain("AbsenceApprovals:Enabled");
        failure.Message.ShouldContain("removed");
    }

    // Off, or left blank, is what every deployment that never used the feature has, and
    // asks for nothing.
    [Theory]
    [Trait("Spec", "CFG-3")]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("")]
    public void AbsenceApprovalsDisabled_Starts(string value)
    {
        using var app = new TestApp(("AbsenceApprovals:Enabled", value));

        Should.NotThrow(() => app.Services);
    }

    [Fact]
    [Trait("Spec", "CFG-3")]
    public void AbsencesApprovePermissionConfigured_StopsStartupSayingTheFeatureWasRemoved()
    {
        using var app = new TestApp(("Auth:Permissions:AbsencesApprove:0", "APPROVER"));

        var failure = StartupFailure(app);

        failure.Message.ShouldContain("Auth:Permissions:AbsencesApprove");
        failure.Message.ShouldContain("removed");
    }

    // The shape before it became a list, which binding would otherwise ignore, leaving every
    // permission gated on no claim at all.
    [Fact]
    [Trait("Spec", "CFG-1")]
    public void PermissionClaimTypeAsASingleName_StopsStartupNamingTheListForm()
    {
        using var app = new TestApp(("Auth:ClaimTypes:Permissions", "roles"));

        StartupFailure(app).Message.ShouldContain("Auth:ClaimTypes:Permissions is now a list");
    }

    // Set by index, as an environment variable would set it. Some configuration sources
    // give the list's parent key an empty value next to the entries, which is not the old
    // shape.
    [Fact]
    [Trait("Spec", "CFG-1")]
    public void PermissionClaimTypesAsAList_Start()
    {
        using var app = new TestApp(
            ("Auth:ClaimTypes:Permissions:0", "roles"),
            ("Auth:ClaimTypes:Permissions:1", "groups"));

        Should.NotThrow(() => app.Services);
    }

    // A blank entry is what shortening a list through environment variables leaves behind,
    // so this is a permission with no claim value at all, among others that have one.
    [Theory]
    [Trait("Spec", "AUTH-5")]
    [InlineData(Permissions.PeopleManage)]
    [InlineData(Permissions.ReportsRead)]
    [InlineData(Permissions.HolidaysSync)]
    [InlineData(Permissions.AuditRead)]
    public void PermissionWithNoClaimValue_StopsStartupNamingIt(string missing)
    {
        using var app = new TestApp(($"Auth:Permissions:{missing}:0", ""));

        var message = StartupFailure(app).Message;

        message.ShouldContain($"Auth:Permissions:{missing} must list");
        foreach (var configured in Permissions.All.Where(p => p != missing))
            message.ShouldNotContain($"Auth:Permissions:{configured}");
    }

    // The defaults in appsettings.json give each permission a value.
    [Fact]
    [Trait("Spec", "AUTH-5")]
    public void EveryPermissionWithAClaimValue_Starts()
    {
        using var app = new TestApp(("Auth:Permissions:AuditRead:0", "AUDITOR"));

        Should.NotThrow(() => app.Services);
    }

    [Fact]
    [Trait("Spec", "AUTH-5")]
    public void UnknownPermission_StopsStartupNamingTheRealOnes()
    {
        using var app = new TestApp(("Auth:Permissions:AuditReader:0", "AUDITOR"));

        var failure = StartupFailure(app);

        failure.Message.ShouldContain("doesn't exist");
        failure.Message.ShouldContain("AuditRead");
    }
}
