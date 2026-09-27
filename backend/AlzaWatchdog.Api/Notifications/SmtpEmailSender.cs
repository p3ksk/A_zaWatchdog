using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AlzaWatchdog.Api.Notifications;

/// <summary>
/// Sends through the configured SMTP account, opening a connection per message.
///
/// MailKit rather than System.Net.Mail: the built-in SmtpClient is documented as
/// obsolete for new code and does not do STARTTLS negotiation properly against
/// modern providers.
/// </summary>
public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsEnabled => _options.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            logger.LogDebug("No SMTP host configured; dropping mail to {To}.", message.To);
            return;
        }

        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mail.To.Add(MailboxAddress.Parse(message.To));
        mail.Subject = message.Subject;
        mail.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();
        
        using var client = new SmtpClient { Timeout = 20_000 };

        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.Auto, ct);

        if (!string.IsNullOrWhiteSpace(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);

        await client.SendAsync(mail, ct);
        await client.DisconnectAsync(true, ct);

        logger.LogInformation("Sent \"{Subject}\" to {To}.", message.Subject, message.To);
    }
}
