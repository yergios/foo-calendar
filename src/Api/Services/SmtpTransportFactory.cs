using MailKit;
using MailKit.Net.Smtp;

using Api.Interfaces;

namespace Api.Services;

public sealed class SmtpTransportFactory : ISmtpTransportFactory
{
    public IMailTransport Create() => new SmtpClient();
}
