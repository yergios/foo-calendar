using Api.Dtos;

namespace Api.Interfaces;

public interface IWebhookPublisher
{
    /// <summary>Queues an event for delivery and returns immediately. Never throws: a
    /// receiver being down, slow or misconfigured must not fail the request that produced
    /// the event, so delivery is deliberately decoupled from it.</summary>
    void Publish(WebhookEvent webhookEvent);
}
