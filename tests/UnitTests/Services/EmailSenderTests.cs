using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Api.Configuration;
using Api.Dtos;
using Api.Interfaces;
using Api.Services;

using MailKit;
using MailKit.Net.Proxy;
using MailKit.Security;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MimeKit;

using Shouldly;

namespace UnitTests.Services;

public sealed class EmailSenderTests
{
    /// <summary>A fake SMTP connection. Only the handful of IMailTransport members
    /// EmailSender actually calls (ConnectAsync, AuthenticateAsync, SendAsync,
    /// DisconnectAsync, Timeout, Dispose) do anything; the rest exist only because the
    /// interface, inherited from the real MailKit client, demands an implementation.
    /// failConnect, given the 1-based connection attempt number, decides whether that
    /// attempt should fail, the same role responder plays in WebhookPublisherTests'
    /// StubHttpMessageHandler.</summary>
    private sealed class FakeMailTransport(Func<int, bool> failConnect = null) : IMailTransport
    {
        public int ConnectAttempts { get; private set; }
        public List<MimeMessage> SentMessages { get; } = [];
        public bool Disposed { get; private set; }

        public int Timeout { get; set; }
        public bool IsConnected { get; private set; }

        public Task ConnectAsync(
            string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
        {
            ConnectAttempts++;
            if (failConnect?.Invoke(ConnectAttempts) == true)
                throw new IOException("connection refused");

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(
            string userName, string password, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> SendAsync(
            MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress progress = default)
        {
            SentMessages.Add(message);
            return Task.FromResult("250 OK");
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        // Nothing below this line is exercised by EmailSender.
        public object SyncRoot => throw new NotSupportedException();
        public SslProtocols SslProtocols { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public CipherSuitesPolicy SslCipherSuitesPolicy { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public TlsCipherSuite? SslCipherSuite => throw new NotSupportedException();
        public X509CertificateCollection ClientCertificates { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public bool CheckCertificateRevocation { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public RemoteCertificateValidationCallback ServerCertificateValidationCallback { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public IPEndPoint LocalEndPoint { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public IProxyClient ProxyClient { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public HashSet<string> AuthenticationMechanisms => throw new NotSupportedException();
        public bool IsAuthenticated => throw new NotSupportedException();
        public bool IsSecure => throw new NotSupportedException();
        public bool IsEncrypted => throw new NotSupportedException();
        public bool IsSigned => throw new NotSupportedException();
        public SslProtocols SslProtocol => throw new NotSupportedException();
        public CipherAlgorithmType? SslCipherAlgorithm => throw new NotSupportedException();
        public int? SslCipherStrength => throw new NotSupportedException();
        public HashAlgorithmType? SslHashAlgorithm => throw new NotSupportedException();
        public int? SslHashStrength => throw new NotSupportedException();
        public ExchangeAlgorithmType? SslKeyExchangeAlgorithm => throw new NotSupportedException();
        public int? SslKeyExchangeStrength => throw new NotSupportedException();

        public event EventHandler<MessageSentEventArgs> MessageSent { add { } remove { } }
        public event EventHandler<ConnectedEventArgs> Connected { add { } remove { } }
        public event EventHandler<DisconnectedEventArgs> Disconnected { add { } remove { } }
        public event EventHandler<AuthenticatedEventArgs> Authenticated { add { } remove { } }

        public string Send(MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress progress = default) => throw new NotSupportedException();
        public string Send(MimeMessage message, MailboxAddress sender, IEnumerable<MailboxAddress> recipients, CancellationToken cancellationToken = default, ITransferProgress progress = default) => throw new NotSupportedException();
        public Task<string> SendAsync(MimeMessage message, MailboxAddress sender, IEnumerable<MailboxAddress> recipients, CancellationToken cancellationToken = default, ITransferProgress progress = default) => throw new NotSupportedException();
        public string Send(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress progress = default) => throw new NotSupportedException();
        public Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress progress = default) => throw new NotSupportedException();
        public string Send(FormatOptions options, MimeMessage message, MailboxAddress sender, IEnumerable<MailboxAddress> recipients, CancellationToken cancellationToken = default, ITransferProgress progress = default) => throw new NotSupportedException();
        public Task<string> SendAsync(FormatOptions options, MimeMessage message, MailboxAddress sender, IEnumerable<MailboxAddress> recipients, CancellationToken cancellationToken = default, ITransferProgress progress = default) => throw new NotSupportedException();
        public void Connect(string host, int port, bool useSsl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Connect(string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Connect(Socket socket, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ConnectAsync(Socket socket, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Connect(Stream stream, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ConnectAsync(Stream stream, string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Authenticate(ICredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AuthenticateAsync(ICredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Authenticate(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Authenticate(Encoding encoding, string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AuthenticateAsync(Encoding encoding, string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Authenticate(string userName, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Authenticate(MailKit.Security.SaslMechanism mechanism, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AuthenticateAsync(MailKit.Security.SaslMechanism mechanism, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Disconnect(bool quit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void NoOp(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task NoOpAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubSmtpTransportFactory(IMailTransport transport) : ISmtpTransportFactory
    {
        public IMailTransport Create() => transport;
    }

    private static EmailMessage SampleMessage() => new(
        "jdoe@example.test", "Jane Doe", "Your Vacation request was approved", "<p>Approved</p>", "Approved");

    private static (EmailSender Sender, FakeMailTransport Transport) CreateSender(
        EmailOptions options = null, Func<int, bool> failConnect = null)
    {
        var transport = new FakeMailTransport(failConnect);
        var sender = new EmailSender(
            new StubSmtpTransportFactory(transport),
            Options.Create(options ?? new EmailOptions { Host = "smtp.example.test", FromAddress = "noreply@example.test" }),
            NullLogger<EmailSender>.Instance);

        return (sender, transport);
    }

    [Fact]
    public async Task DeliverAsync_SendsTheMessageToTheTransport()
    {
        var (sender, transport) = CreateSender();

        await sender.DeliverAsync(SampleMessage());

        var sent = transport.SentMessages.ShouldHaveSingleItem();
        sent.To.Mailboxes.Single().Address.ShouldBe("jdoe@example.test");
        sent.Subject.ShouldBe("Your Vacation request was approved");
    }

    [Fact]
    public async Task DeliverAsync_RetriesOnFailureAndGivesUpAfterMaxAttempts()
    {
        var (sender, transport) = CreateSender(
            new EmailOptions { Host = "smtp.example.test", FromAddress = "noreply@example.test", MaxAttempts = 2 },
            failConnect: _ => true);

        await sender.DeliverAsync(SampleMessage());

        transport.ConnectAttempts.ShouldBe(2);
        transport.SentMessages.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeliverAsync_WhenTheTransportThrows_DoesNotPropagate()
    {
        var (sender, _) = CreateSender(
            new EmailOptions { Host = "smtp.example.test", FromAddress = "noreply@example.test", MaxAttempts = 1 },
            failConnect: _ => true);

        await Should.NotThrowAsync(() => sender.DeliverAsync(SampleMessage()));
    }

    [Fact]
    public async Task DeliverAsync_SucceedsOnceAFailedAttemptIsRetried()
    {
        var (sender, transport) = CreateSender(
            new EmailOptions { Host = "smtp.example.test", FromAddress = "noreply@example.test", MaxAttempts = 3 },
            failConnect: attempt => attempt == 1);

        await sender.DeliverAsync(SampleMessage());

        transport.SentMessages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Send_DeliversTheQueuedMessageOnceTheServiceIsRunning()
    {
        var (sender, transport) = CreateSender();

        await sender.StartAsync(CancellationToken.None);
        try
        {
            sender.Send(SampleMessage());

            // The delivery loop runs on the background service's own task; give it a
            // moment to drain the one message just queued.
            for (var i = 0; i < 50 && transport.SentMessages.Count == 0; i++)
                await Task.Delay(20);
        }
        finally
        {
            await sender.StopAsync(CancellationToken.None);
        }

        transport.SentMessages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Send_WhenNoHostIsConfigured_DeliversNothing()
    {
        var (sender, transport) = CreateSender(new EmailOptions());

        await sender.StartAsync(CancellationToken.None);
        try
        {
            sender.Send(SampleMessage());
            await Task.Delay(200);
        }
        finally
        {
            await sender.StopAsync(CancellationToken.None);
        }

        transport.SentMessages.ShouldBeEmpty();
    }
}
