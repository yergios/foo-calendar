using MailKit;

namespace Api.Interfaces;

/// <summary>Builds the SMTP connection EmailSender delivers each message over. A factory
/// rather than a shared client: MailKit's transport isn't meant to be reused across
/// concurrent sends, and this is the one seam a test needs to fake a mail server instead of
/// talking to a real one, the same role IHttpClientFactory plays for WebhookPublisher.
/// </summary>
public interface ISmtpTransportFactory
{
    IMailTransport Create();
}
