namespace AlzaWatchdog.Api.Notifications;

/// <summary>One message, in both the shapes a mail client may choose to render.</summary>
public record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

public interface IEmailSender
{
    /// <summary>False when no SMTP host is configured, in which case nothing is sent.</summary>
    bool IsEnabled { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
