namespace Api.Configuration;

/// <summary>SMTP delivery for the three opt-in email categories (absence reviewed, new
/// request waiting for review, weekly "who's off" digest). Deliberately one shared SMTP
/// relay rather than a provider-specific integration, the same reasoning as
/// <see cref="WebhookOptions"/>: whatever actually sends the mail is somebody else's
/// server, this just has to speak SMTP to it.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The SMTP relay's hostname. Empty switches every email off entirely,
    /// including the weekly digest scheduler, which is the default.</summary>
    public string Host { get; init; } = "";

    public int Port { get; init; } = 587;

    /// <summary>Whether to negotiate STARTTLS after connecting. Off is only for a relay
    /// that already sits on a trusted, unencrypted network, e.g. a local dev mailbox
    /// catcher.</summary>
    public bool UseStartTls { get; init; } = true;

    /// <summary>Left blank for a relay that accepts anonymous submission.</summary>
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";

    public string FromAddress { get; init; } = "";
    public string FromName { get; init; } = "";

    /// <summary>The app's own public URL, so an email can link back into it. Blank omits
    /// the link rather than pointing at nothing, the same "empty means leave it out" idiom
    /// as <see cref="WebhookOptions.Secret"/>.</summary>
    public string AppUrl { get; init; } = "";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Delivery attempts per email, including the first.</summary>
    public int MaxAttempts { get; init; } = 3;

    public WeeklyDigestOptions WeeklyDigest { get; init; } = new();
}

/// <summary>When the "who's off this week" digest goes out, for whoever has opted in.
/// Interpreted against the server process's own local clock: this app has no notion of a
/// deployment's timezone anywhere else, so a deployment that cares sets its container's TZ.
/// </summary>
public sealed class WeeklyDigestOptions
{
    public bool Enabled { get; init; } = true;
    public DayOfWeek DayOfWeek { get; init; } = DayOfWeek.Monday;
    public TimeSpan TimeOfDay { get; init; } = TimeSpan.FromHours(7);
}
