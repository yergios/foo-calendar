using System.Security.Claims;

using Api.Services;

using Shouldly;

namespace UnitTests.Services;

public sealed class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

    [Fact]
    [Trait("Spec", "AUTH-4")]
    public void HasAnyPermission_WhenTheClaimCarriesAnAcceptedValue_IsTrue()
    {
        var user = User(("permissions", "APP_FOO_CALENDAR_ADMIN"));

        user.HasAnyPermission(["permissions"], ["APP_FOO_CALENDAR_ADMIN"]).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "AUTH-4")]
    public void HasAnyPermission_MatchesAnyOfSeveralAcceptedValues()
    {
        var user = User(("roles", "leave-admin"));

        user.HasAnyPermission(["roles"], ["hr-admin", "leave-admin"]).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "AUTH-4")]
    public void HasAnyPermission_SearchesEveryConfiguredClaimName()
    {
        // Entra ID puts app roles in "roles" and group ids in "groups"; a deployment can
        // grant admin through either.
        var user = User(("groups", "8f4c2f1a-0000-4c1e-9a3e-1c2d3e4f5a6b"));

        user.HasAnyPermission(
            ["roles", "groups"],
            ["8f4c2f1a-0000-4c1e-9a3e-1c2d3e4f5a6b"]).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "AUTH-4")]
    public void HasAnyPermission_IgnoresValueCasing()
    {
        var user = User(("groups", "8F4C2F1A-0000-4C1E-9A3E-1C2D3E4F5A6B"));

        user.HasAnyPermission(["groups"], ["8f4c2f1a-0000-4c1e-9a3e-1c2d3e4f5a6b"]).ShouldBeTrue();
    }

    [Fact]
    [Trait("Spec", "AUTH-4")]
    public void HasAnyPermission_WhenTheValueSitsInAnUnconfiguredClaim_IsFalse()
    {
        var user = User(("groups", "leave-admin"));

        user.HasAnyPermission(["roles"], ["leave-admin"]).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "AUTH-4")]
    public void HasAnyPermission_WhenTheUserHoldsOnlyOtherValues_IsFalse()
    {
        var user = User(("roles", "reader"), ("roles", "writer"));

        user.HasAnyPermission(["roles"], ["leave-admin"]).ShouldBeFalse();
    }

    [Fact]
    [Trait("Spec", "AUTH-4")]
    public void HasAnyPermission_WhenNoValuesAreAccepted_IsFalse()
    {
        var user = User(("roles", "leave-admin"));

        user.HasAnyPermission(["roles"], []).ShouldBeFalse();
    }
}
