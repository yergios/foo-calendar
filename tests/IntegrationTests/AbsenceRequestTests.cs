using System.Net;
using System.Net.Http.Json;

using IntegrationTests.TestSupport;

using Shouldly;

namespace IntegrationTests;

public sealed class AbsenceRequestTests
{
    // Through the endpoint rather than Validator.TryValidateObject: the limit sits on the
    // record's constructor parameter, which only the minimal API validation reads. A request
    // that got past it would reach the handler and answer 404, the caller having no profile.
    [Theory]
    [Trait("Spec", "ABS-3")]
    [InlineData(500, HttpStatusCode.NotFound)]
    [InlineData(501, HttpStatusCode.BadRequest)]
    public async Task ANoteOverFiveHundredCharacters_IsABadRequest(int length, HttpStatusCode expected)
    {
        using var app = new TestApp();
        using var client = app.CreateClient();

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/absences", UriKind.Relative),
            new { startDate = "2026-03-02", endDate = "2026-03-06", type = "Vacation", note = new string('x', length) });

        response.StatusCode.ShouldBe(expected);
    }
}
