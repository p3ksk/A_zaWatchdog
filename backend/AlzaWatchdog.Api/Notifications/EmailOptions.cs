namespace AlzaWatchdog.Api.Notifications;

/// <summary>
/// The SMTP account notifications are sent from. Everything here comes from
/// configuration, so a deployment that sets no host simply sends no mail — there
/// is no separate on/off switch to contradict it.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? User { get; set; }

    public string? Password { get; set; }

    /// <summary>The From address. Falls back to <see cref="User"/> when that is an address.</summary>
    public string? From { get; set; }

    /// <summary>The name shown beside the From address.</summary>
    public string FromName { get; set; } = "Alza Watchdog";

    /// <summary>
    /// Public origin of the site, used to build the confirmation link. Only needed
    /// when the API cannot tell from the request it is handling — behind a proxy
    /// that terminates TLS, the request arrives as plain http, and a link built
    /// from it would say http to the reader.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Nothing is sent until there is a host to send it through and an address to send it from.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    public string? FromAddress => string.IsNullOrWhiteSpace(From) ? User : From;
}
