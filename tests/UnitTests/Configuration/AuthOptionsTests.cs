using Api.Configuration;

using Shouldly;

namespace UnitTests.Configuration;

public sealed class AuthOptionsTests
{
    private static AuthOptions WithPermissions(params (string Permission, string[] Values)[] entries)
    {
        var options = new AuthOptions();
        foreach (var (permission, values) in entries)
            options.Permissions[permission] = [.. values];

        return options;
    }

    [Fact]
    public void ValuesFor_WhenThePermissionIsNotConfigured_GrantsNothing()
    {
        var options = WithPermissions((Permissions.PeopleManage, ["ADMIN"]));

        options.ValuesFor(Permissions.AuditRead).ShouldBeEmpty();
    }

    // Configuration keys are case-insensitive everywhere else in .NET, and a deployment
    // writing "peoplemanage" shouldn't silently end up granting nobody anything.
    [Fact]
    public void ValuesFor_MatchesThePermissionNameWhateverItsCasing()
    {
        var options = WithPermissions(("peoplemanage", ["ADMIN"]));

        options.ValuesFor(Permissions.PeopleManage).ShouldBe(["ADMIN"]);
    }

    [Fact]
    public void AllPermissionValues_DeduplicatesTheValueSharedBySeveralPermissions()
    {
        var options = WithPermissions(
            (Permissions.PeopleManage, ["ADMIN"]),
            (Permissions.HolidaysSync, ["ADMIN", "COORDINATOR"]));

        options.AllPermissionValues().ShouldBe(["ADMIN", "COORDINATOR"], ignoreOrder: true);
    }

    [Fact]
    public void EffectiveDevPermissions_WhenGrantingEverything_HoldsEveryConfiguredValue()
    {
        var options = WithPermissions(
            (Permissions.PeopleManage, ["ADMIN"]),
            (Permissions.HolidaysSync, ["COORDINATOR"]));

        options.EffectiveDevPermissions().ShouldBe(["ADMIN", "COORDINATOR"], ignoreOrder: true);
    }

    [Fact]
    public void EffectiveDevPermissions_WhenNotGrantingEverything_HoldsOnlyWhatTheDevUserLists()
    {
        var options = WithPermissions(
            (Permissions.PeopleManage, ["ADMIN"]),
            (Permissions.HolidaysSync, ["COORDINATOR"]));

        var asACoordinator = new AuthOptions
        {
            Permissions = options.Permissions,
            DevUser = new DevUserOptions { GrantAllPermissions = false, Permissions = ["COORDINATOR"] },
        };

        asACoordinator.EffectiveDevPermissions().ShouldBe(["COORDINATOR"]);
    }

    // Shortening a list through environment variables blanks the leftover indexes rather
    // than removing them, and a blank accepted value would match a claim with no value.
    [Fact]
    public void RemoveBlankEntries_DropsBlanksFromEveryPermission()
    {
        var options = WithPermissions((Permissions.PeopleManage, ["ADMIN", "", "  "]));

        options.RemoveBlankEntries();

        options.ValuesFor(Permissions.PeopleManage).ShouldBe(["ADMIN"]);
    }
}
