using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Api.Configuration;
using Api.Data.Models;
using Api.Dtos;
using Api.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

namespace UnitTests.Services;

public sealed class WebhookPublisherTests
{
    private const string Url = "https://hooks.example.test/absences";

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        Dictionary<string, string> Headers,
        string Body);

    private sealed class StubHttpMessageHandler(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                body));

            return responder(Requests.Count);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static WebhookEvent SampleEvent() => new(
        "11111111-1111-1111-1111-111111111111",
        WebhookEvents.AbsenceCreated,
        new DateTime(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc),
        "jdoe",
        new WebhookAbsence(7, 3, "Jane Doe", new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 8), AbsenceType.Vacation, "Trip"));

    private static (WebhookPublisher Publisher, StubHttpMessageHandler Handler) CreatePublisher(
        WebhookOptions options = null,
        Func<int, HttpResponseMessage> responder = null)
    {
        var handler = new StubHttpMessageHandler(responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var publisher = new WebhookPublisher(
            new StubHttpClientFactory(handler),
            Options.Create(options ?? new WebhookOptions { Url = Url }),
            NullLogger<WebhookPublisher>.Instance);

        return (publisher, handler);
    }

    [Fact]
    public async Task DeliverAsync_PostsTheEventToTheConfiguredUrl()
    {
        var (publisher, handler) = CreatePublisher();

        await publisher.DeliverAsync(SampleEvent());

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ToString().ShouldBe(Url);
        request.Headers["X-FooCalendar-Event"].ShouldBe("absence.created");
        request.Headers["X-FooCalendar-Delivery"].ShouldBe("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task DeliverAsync_SerializesTheEventAsCamelCaseWithReadableEnums()
    {
        var (publisher, handler) = CreatePublisher();

        await publisher.DeliverAsync(SampleEvent());

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        var root = body.RootElement;
        root.GetProperty("event").GetString().ShouldBe("absence.created");
        root.GetProperty("actor").GetString().ShouldBe("jdoe");

        var absence = root.GetProperty("absence");
        absence.GetProperty("personName").GetString().ShouldBe("Jane Doe");
        absence.GetProperty("startDate").GetString().ShouldBe("2026-05-04");
        absence.GetProperty("type").GetString().ShouldBe("Vacation");
    }

    [Fact]
    public async Task DeliverAsync_WhenASecretIsConfigured_SignsTheExactBodySent()
    {
        var (publisher, handler) = CreatePublisher(new WebhookOptions { Url = Url, Secret = "s3cret" });

        await publisher.DeliverAsync(SampleEvent());

        var request = handler.Requests.ShouldHaveSingleItem();
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("s3cret"),
            Encoding.UTF8.GetBytes(request.Body)));

        request.Headers["X-FooCalendar-Signature"].ShouldBe($"sha256={expected}");
    }

    [Fact]
    public async Task DeliverAsync_WhenNoSecretIsConfigured_SendsNoSignature()
    {
        var (publisher, handler) = CreatePublisher();

        await publisher.DeliverAsync(SampleEvent());

        handler.Requests.Single().Headers.ShouldNotContainKey("X-FooCalendar-Signature");
    }

    [Fact]
    public async Task DeliverAsync_RetriesOnFailureAndGivesUpAfterMaxAttempts()
    {
        var (publisher, handler) = CreatePublisher(
            new WebhookOptions { Url = Url, MaxAttempts = 2 },
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await publisher.DeliverAsync(SampleEvent());

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheReceiverThrows_DoesNotPropagate()
    {
        var (publisher, handler) = CreatePublisher(
            new WebhookOptions { Url = Url, MaxAttempts = 1 },
            _ => throw new HttpRequestException("connection refused"));

        await Should.NotThrowAsync(() => publisher.DeliverAsync(SampleEvent()));
        handler.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Publish_DeliversTheQueuedEventOnceTheServiceIsRunning()
    {
        var delivered = new TaskCompletionSource();
        var (publisher, handler) = CreatePublisher(responder: _ =>
        {
            delivered.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await publisher.StartAsync(CancellationToken.None);
        try
        {
            publisher.Publish(SampleEvent());
            await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await publisher.StopAsync(CancellationToken.None);
        }

        handler.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Publish_WhenNoUrlIsConfigured_DeliversNothing()
    {
        var (publisher, handler) = CreatePublisher(new WebhookOptions());

        await publisher.StartAsync(CancellationToken.None);
        try
        {
            publisher.Publish(SampleEvent());
            await Task.Delay(200);
        }
        finally
        {
            await publisher.StopAsync(CancellationToken.None);
        }

        handler.Requests.ShouldBeEmpty();
    }

    // A note on a sick leave is health information, and a webhook is usually pointed at a
    // chat channel far wider than the people it was written for.
    [Fact]
    public async Task DeliverAsync_LeavesTheAbsenceNoteOutByDefault()
    {
        var (publisher, handler) = CreatePublisher();

        await publisher.DeliverAsync(SampleEvent());

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        var absence = body.RootElement.GetProperty("absence");

        // Still in the payload, so a receiver reading it doesn't break; simply null.
        absence.GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        absence.GetProperty("type").GetString().ShouldBe("Vacation");
    }

    [Fact]
    public async Task DeliverAsync_WhenNotesAreOptedIn_SendsTheNote()
    {
        var (publisher, handler) = CreatePublisher(
            new WebhookOptions { Url = Url, IncludeAbsenceNote = true });

        await publisher.DeliverAsync(SampleEvent());

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        body.RootElement.GetProperty("absence").GetProperty("note").GetString().ShouldBe("Trip");
    }
}
