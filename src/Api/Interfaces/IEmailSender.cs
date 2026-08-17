using Api.Dtos;

namespace Api.Interfaces;

public interface IEmailSender
{
    /// <summary>Queues an email for delivery and returns immediately. Never throws: an
    /// SMTP server being down, slow or misconfigured must not fail the request that
    /// produced the email, so delivery is deliberately decoupled from it.</summary>
    void Send(EmailMessage message);
}
