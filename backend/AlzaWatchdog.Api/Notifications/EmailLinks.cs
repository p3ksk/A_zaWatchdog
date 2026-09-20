namespace AlzaWatchdog.Api.Notifications;

/// <summary>Where the links in an email point.</summary>
public static class EmailLinks
{
    /// <summary>
    /// The site's public origin. Configuration wins, because behind a proxy that
    /// terminates TLS the request itself arrives as plain http on an internal
    /// hostname, and a link built from that is one the reader cannot follow.
    /// </summary>
    public static string PublicBase(EmailOptions options, HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            return options.BaseUrl.TrimEnd('/');

        var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;

        return $"{scheme}://{request.Host}";
    }

    public static string Confirm(string publicBase, string token) =>
        $"{publicBase}/api/users/email/confirm/{token}";
}
