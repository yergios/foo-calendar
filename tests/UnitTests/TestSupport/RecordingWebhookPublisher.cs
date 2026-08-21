using Api.Dtos;
using Api.Interfaces;

namespace UnitTests.TestSupport;

/// <summary>Records what the service published instead of delivering anything.</summary>
public sealed class RecordingWebhookPublisher : IWebhookPublisher
{
    public List<WebhookEvent> Published { get; } = [];

    public void Publish(WebhookEvent webhookEvent) => Published.Add(webhookEvent);
}
