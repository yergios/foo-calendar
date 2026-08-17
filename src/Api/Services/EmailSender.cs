using System.Threading.Channels;

using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;

using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

namespace Api.Services;

/// <summary>
/// Queues opt-in emails and delivers them over SMTP.
///
/// Shaped after WebhookPublisher: both halves live here because they are two ends of one
/// queue, callers hand messages in from whatever request or background job produced them,
/// and the background loop drains that queue on its own schedule. Delivery is best effort
/// by design, for the same reason WebhookPublisher's is: the queue is in memory, so a
/// restart drops whatever is still in it, and an SMTP relay that stays down loses messages
/// rather than blocking the app.
/// </summary>
public sealed class EmailSender(
    ISmtpTransportFactory transportFactory,
    IOptions<EmailOptions> options,
    ILogger<EmailSender> logger) : BackgroundService, IEmailSender
{
    // Same reasoning as WebhookPublisher's queue: enough to absorb a burst (a weekly digest
    // fans out one message per recipient) while the relay is briefly slow, small enough
    // that a dead one can't grow this without limit.
    private const int QueueCapacity = 256;

    private readonly Channel<EmailMessage> queue = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.Host);

    public void Send(EmailMessage message)
    {
        if (!IsEnabled) return;

        if (!queue.Writer.TryWrite(message))
            logger.LogWarning("Dropped email to {To}: the queue is full.", message.ToAddress);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled) return;

        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            // One at a time: ordering doesn't matter here, but a handful of emails a day
            // doesn't call for anything more than the simplicity of a single delivery loop.
            await DeliverAsync(message, stoppingToken);
        }
    }

    /// <summary>Sends one email, retrying a few times before giving up on it. Never throws:
    /// a failed delivery is logged and dropped rather than taking the loop down with it.
    /// </summary>
    public async Task DeliverAsync(EmailMessage message, CancellationToken ct = default)
    {
        var config = options.Value;
        var mime = BuildMime(config, message);

        for (var attempt = 1; attempt <= config.MaxAttempts; attempt++)
        {
            try
            {
                using var transport = transportFactory.Create();
                transport.Timeout = (int)config.Timeout.TotalMilliseconds;

                await transport.ConnectAsync(
                    config.Host, config.Port,
                    config.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
                    ct);

                if (!string.IsNullOrWhiteSpace(config.Username))
                    await transport.AuthenticateAsync(config.Username, config.Password, ct);

                await transport.SendAsync(mime, ct);
                await transport.DisconnectAsync(quit: true, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Email to {To} attempt {Attempt}/{MaxAttempts} failed.",
                    message.ToAddress, attempt, config.MaxAttempts);
            }

            if (attempt < config.MaxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
        }

        logger.LogError("Email to {To} gave up after {MaxAttempts} attempts.",
            message.ToAddress, config.MaxAttempts);
    }

    private static MimeMessage BuildMime(EmailOptions config, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();
        return mime;
    }
}
