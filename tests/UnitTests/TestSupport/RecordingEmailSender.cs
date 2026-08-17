using Api.Dtos;
using Api.Interfaces;

namespace UnitTests.TestSupport;

/// <summary>Keeps what a service asked to be emailed, so a test can assert on it without
/// an SMTP server. Actual delivery, retry, and give-up behavior are EmailSender's own job
/// and are tested there.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public void Send(EmailMessage message) => Sent.Add(message);
}
