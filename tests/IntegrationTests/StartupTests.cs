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
    [Trait("Spec", "CFG-1")]
    public void InvalidSetting_StopsStartupNamingIt_WithTheDatabaseUntouched()
    {
        using var app = new TestApp(("Team:HomeRegion", "Zurich"));
        File.WriteAllBytes(app.DatabasePath, []);

        StartupFailure(app).Message.ShouldContain("Team:HomeRegion");

        app.TableNames().ShouldBeEmpty();
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

    [Fact]
    [Trait("Spec", "CFG-3")]
    public void AbsenceApprovalsEnabled_StopsStartupSayingTheFeatureWasRemoved()
    {
        using var app = new TestApp(("AbsenceApprovals:Enabled", "true"));

        var failure = StartupFailure(app);

        failure.Message.ShouldContain("AbsenceApprovals:Enabled");
        failure.Message.ShouldContain("removed");
    }

    // Off is what every deployment that never used the feature has, and asks for nothing.
    [Fact]
    [Trait("Spec", "CFG-3")]
    public void AbsenceApprovalsDisabled_Starts()
    {
        using var app = new TestApp(("AbsenceApprovals:Enabled", "false"));

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
    // so this is a permission with no claim value at all.
    [Fact]
    [Trait("Spec", "AUTH-5")]
    public void PermissionWithNoClaimValue_StopsStartupNamingWhatToConfigure()
    {
        using var app = new TestApp(("Auth:Permissions:AuditRead:0", ""));

        var failure = StartupFailure(app);

        failure.Message.ShouldContain("Auth:Permissions");
        failure.Message.ShouldContain("AuditRead");
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
