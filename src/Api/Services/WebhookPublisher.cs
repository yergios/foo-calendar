using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;

using Microsoft.Extensions.Options;

namespace Api.Services;

/// <summary>
/// Queues absence events and delivers them to the configured webhook.
///
/// Both halves live here because they are two ends of one queue: callers hand events in
/// from whatever request produced them, and the background loop drains that queue on its
/// own schedule. Nothing about a delivery is allowed to reach back into the request.
///
/// Delivery is best effort by design. The queue is in memory, so a restart drops whatever
/// is still in it, and a receiver that stays down loses events rather than blocking the
/// app. Anything needing stronger guarantees should treat these as hints and reconcile
/// against the API.
/// </summary>
public sealed class WebhookPublisher(
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> options,
    ILogger<WebhookPublisher> logger) : BackgroundService, IWebhookPublisher
{
    public const string HttpClientName = "webhook";

    // Enough to absorb a burst while a receiver is briefly slow, small enough that a dead
    // one can't grow this without limit. Dropping the oldest keeps the newest flowing,
    // which is the more useful end to keep for notifications.
    private const int QueueCapacity = 256;

    // Matches what the API itself returns, so a receiver sees "Vacation" rather than 0 and
    // the same camelCase field names as the REST responses.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Channel<WebhookEvent> queue = Channel.CreateBounded<WebhookEvent>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.Url);

    public void Publish(WebhookEvent webhookEvent)
    {
        if (!IsEnabled) return;

        if (!queue.Writer.TryWrite(webhookEvent))
            logger.LogWarning("Dropped webhook event {Event} {Id}: the queue is full.",
                webhookEvent.Event, webhookEvent.Id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled) return;

        await foreach (var webhookEvent in queue.Reader.ReadAllAsync(stoppingToken))
        {
            // One at a time: ordering is easier to reason about than throughput here, and
            // these are a handful of events a day, not a stream.
            await DeliverAsync(webhookEvent, stoppingToken);
        }
    }

    /// <summary>Posts one event, retrying a few times before giving up on it. Never throws:
    /// a failed delivery is logged and dropped rather than taking the loop down with it.
    /// </summary>
    public async Task DeliverAsync(WebhookEvent webhookEvent, CancellationToken ct = default)
    {
        var config = options.Value;

        // Dropped here rather than at each call site: this is the last thing before the
        // wire, so it is the only place that has to remember. The fields stay in the
        // payload, they simply arrive null, see WebhookOptions.IncludeAbsenceNote.
        if (!config.IncludeAbsenceNote && webhookEvent.Absence is not null)
            webhookEvent = webhookEvent with
            {
                Absence = webhookEvent.Absence with { Note = null, ReviewNote = null },
            };

        var body = JsonSerializer.Serialize(webhookEvent, JsonOptions);

        for (var attempt = 1; attempt <= config.MaxAttempts; attempt++)
        {
            try
            {
                using var client = httpClientFactory.CreateClient(HttpClientName);
                client.Timeout = config.Timeout;

                using var request = BuildRequest(config, webhookEvent, body);
                using var response = await client.SendAsync(request, ct);

                if (response.IsSuccessStatusCode) return;

                logger.LogWarning(
                    "Webhook {Event} {Id} attempt {Attempt}/{MaxAttempts} returned {Status}.",
                    webhookEvent.Event, webhookEvent.Id, attempt, config.MaxAttempts,
                    (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Webhook {Event} {Id} attempt {Attempt}/{MaxAttempts} failed.",
                    webhookEvent.Event, webhookEvent.Id, attempt, config.MaxAttempts);
            }

            if (attempt < config.MaxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }

        logger.LogError("Webhook {Event} {Id} gave up after {MaxAttempts} attempts.",
            webhookEvent.Event, webhookEvent.Id, config.MaxAttempts);
    }

    private static HttpRequestMessage BuildRequest(
        WebhookOptions config, WebhookEvent webhookEvent, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        // Both are also in the body; as headers they let a receiver route or deduplicate
        // without parsing anything.
        request.Headers.Add("X-FooCalendar-Event", webhookEvent.Event);
        request.Headers.Add("X-FooCalendar-Delivery", webhookEvent.Id);

        if (!string.IsNullOrWhiteSpace(config.Secret))
            request.Headers.Add("X-FooCalendar-Signature", Sign(config.Secret, body));

        return request;
    }

    /// <summary>HMAC-SHA256 of the exact bytes sent, keyed with the shared secret. The
    /// receiver recomputes it over the raw body it read, before parsing.</summary>
    public static string Sign(string secret, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }
}
