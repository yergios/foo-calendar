using System.Net;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

public sealed class ReadRangeTests
{
    private const string Absences = "/api/absences";
    private const string Catalog = "/api/bank-holidays/catalog";

    private static async Task<HttpResponseMessage> GetAsync(string path, string from, string to)
    {
        using var app = new TestApp();
        using var client = app.CreateClient();

        return await client.GetAsync(new Uri($"{path}?from={from}&to={to}", UriKind.Relative));
    }

    private static async Task ShouldBeRejectedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"status\":400");
    }

    // A leap year, both ends included: the most the SPA ever asks for.
    [Theory]
    [Trait("Spec", "ABS-8")]
    [Trait("Spec", "HOL-10")]
    [InlineData(Absences)]
    [InlineData(Catalog)]
    public async Task ALeapYear_IsServed(string path)
    {
        using var response = await GetAsync(path, "2028-01-01", "2028-12-31");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [Trait("Spec", "ABS-8")]
    [Trait("Spec", "HOL-10")]
    [InlineData(Absences)]
    [InlineData(Catalog)]
    public async Task ARangeOf367Days_IsABadRequest(string path)
    {
        using var response = await GetAsync(path, "2026-01-01", "2027-01-02");

        await ShouldBeRejectedAsync(response);
    }

    [Theory]
    [Trait("Spec", "ABS-8")]
    [Trait("Spec", "HOL-10")]
    [InlineData(Absences)]
    [InlineData(Catalog)]
    public async Task ToBeforeFrom_IsABadRequest(string path)
    {
        using var response = await GetAsync(path, "2026-03-02", "2026-03-01");

        await ShouldBeRejectedAsync(response);
    }

    [Theory]
    [Trait("Spec", "ABS-8")]
    [Trait("Spec", "HOL-10")]
    [InlineData(Absences)]
    [InlineData(Catalog)]
    public async Task ASingleDay_IsServed(string path)
    {
        using var response = await GetAsync(path, "2026-03-01", "2026-03-01");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
