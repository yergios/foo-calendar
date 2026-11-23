using Api.Configuration;
using Api.Data;

using IntegrationTests.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
