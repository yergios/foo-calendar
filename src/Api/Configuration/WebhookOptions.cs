namespace Api.Configuration;

/// <summary>One outgoing HTTP hook for absence events. Deliberately generic rather than a
/// set of per-vendor connectors: whatever a team wants events to end up in - Slack, Teams,
/// email, a ticketing system - is a small script on their side, not a dependency here.
/// </summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>Where events are POSTed. Empty switches delivery off entirely, which is
    /// the default.</summary>
    public string Url { get; init; } = "";

    /// <summary>Shared secret for the X-FooCalendar-Signature header, so the receiver can
    /// tell a real event from anyone who has learned the URL. Empty sends no signature.
    /// </summary>
    public string Secret { get; init; } = "";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Delivery attempts per event, including the first.</summary>
    public int MaxAttempts { get; init; } = 3;
}
