using System.Net;
using System.Text.Json;

using Api.Services;

using IntegrationTests.TestSupport;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using Shouldly;

namespace IntegrationTests;

/// <summary>
/// What a failure looks like to the caller, from a route only the test maps, which throws
/// exactly the exception under test. A domain error is the rule's answer, with its message;
/// anything else is a bug, answered 500 with nothing about the exception, which goes to the
/// log instead.
/// </summary>
public sealed class ErrorResponseTests
{
    private const string ProblemJson = "application/problem+json";

    private const string Message = "The rule's own words, or a secret the code let slip.";

    private static Exception Exception(string kind) => kind switch
    {
        nameof(InvalidInputException) => new InvalidInputException(Message),
        nameof(NotOwnerException) => new NotOwnerException(Message),
        nameof(NotFoundException) => new NotFoundException(Message),
        nameof(ConflictException) => new ConflictException(Message),
        nameof(ArgumentException) => new ArgumentException(Message),
        nameof(UnauthorizedAccessException) => new UnauthorizedAccessException(Message),
        nameof(KeyNotFoundException) => new KeyNotFoundException(Message),
        nameof(InvalidOperationException) => new InvalidOperationException(Message),
        nameof(BadHttpRequestException) => new BadHttpRequestException(
            Message, StatusCodes.Status413PayloadTooLarge),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such exception in this test."),
    };

    /// <summary>The app as a deployment runs it, plus a route that throws the named
    /// exception. Anonymous, so reaching it takes no token.</summary>
    private static TestApp Throwing(string kind, string environment = "Production") => new(
        ("Auth:Authority", "https://idp.example.test/"),
        ("Auth:Audience", "foo-calendar"),
        ("Auth:ClientId", "foo-calendar-spa"))
    {
        Environment = environment,
        Endpoints = routes => routes
            .MapGet("/api/test/throw", string () => throw Exception(kind))
            .AllowAnonymous(),
    };

    private static async Task<(HttpStatusCode Status, string? ContentType, string Body)> GetAsync(TestApp app)
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/test/throw", UriKind.Relative));
        return (response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [Trait("Spec", "ERR-1")]
    [InlineData(nameof(InvalidInputException), HttpStatusCode.BadRequest, "Invalid request")]
    [InlineData(nameof(NotOwnerException), HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(nameof(NotFoundException), HttpStatusCode.NotFound, "Not found")]
    [InlineData(nameof(ConflictException), HttpStatusCode.Conflict, "Conflict")]
    public async Task ADomainError_IsAnsweredWithItsStatusAndItsMessage(
        string kind, HttpStatusCode status, string title)
    {
        using var app = Throwing(kind);

        var response = await GetAsync(app);

        response.Status.ShouldBe(status);
        response.ContentType.ShouldBe(ProblemJson);
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.RootElement.GetProperty("title").GetString().ShouldBe(title);
        problem.RootElement.GetProperty("detail").GetString().ShouldBe(Message);
    }

    // The four the handler used to map by type, which framework and library code throw too:
    // none of them is the domain's answer any more than any other exception is.
    // Development too: the exception handler runs before the developer exception page
    // could, so a developer finds the exception in the log, as an operator does.
    [Theory]
    [Trait("Spec", "ERR-1")]
    [Trait("Spec", "ERR-2")]
    [InlineData(nameof(ArgumentException), "Production")]
    [InlineData(nameof(UnauthorizedAccessException), "Production")]
    [InlineData(nameof(KeyNotFoundException), "Production")]
    [InlineData(nameof(InvalidOperationException), "Production")]
    [InlineData(nameof(InvalidOperationException), "Development")]
    public async Task AnyOtherException_IsA500SayingNothingAboutIt_AndIsLogged(string kind, string environment)
    {
        using var app = Throwing(kind, environment);

        var response = await GetAsync(app);

        response.Status.ShouldBe(HttpStatusCode.InternalServerError);
        response.ContentType.ShouldBe(ProblemJson);
        response.Body.ShouldNotContain(Message);
        response.Body.ShouldNotContain(kind);
        response.Body.ShouldNotContain(nameof(ErrorResponseTests));
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(500);
        problem.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
        problem.RootElement.TryGetProperty("exception", out _).ShouldBeFalse();

        app.Logs.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error
            && entry.Exception != null
            && entry.Exception.GetType().Name == kind
            && entry.Exception.Message == Message);
    }

    // What Kestrel throws for a body over its size limit, outside Development as much as in
    // it: the status it carries is the answer, still without the exception's message.
    [Fact]
    [Trait("Spec", "ERR-2")]
    public async Task ABadHttpRequestException_IsAnsweredWithItsStatus_SayingNothingAboutIt()
    {
        using var app = Throwing(nameof(BadHttpRequestException));

        var response = await GetAsync(app);

        response.Status.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.ContentType.ShouldBe(ProblemJson);
        response.Body.ShouldNotContain(Message);
        response.Body.ShouldNotContain(nameof(BadHttpRequestException));
        using var problem = JsonDocument.Parse(response.Body);
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(413);
        problem.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
        problem.RootElement.TryGetProperty("exception", out _).ShouldBeFalse();
    }
}
