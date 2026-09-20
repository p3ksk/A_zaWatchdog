using System.Globalization;
using System.Net;
using System.Text;

namespace AlzaWatchdog.Api.Notifications;

/// <summary>What one product did between the last reading someone was told about and this one.</summary>
public record ProductChange(
    string Name,
    string Url,
    string? Currency,
    decimal? OldPrice,
    decimal? NewPrice,
    decimal? OldCouponPrice,
    decimal? NewCouponPrice,
    decimal? OldPlusPrice,
    decimal? NewPlusPrice,
    string? OldAvailability,
    string? NewAvailability);

/// <summary>
/// Turns changes into the two bodies a mail carries. Pure text in, pure text out —
/// no database and no SMTP — so what the reader ends up seeing can be asserted on
/// directly in tests.
/// </summary>
public static class MailComposer
{
    /// <summary>
    /// Lines past this are summarised rather than listed. A sweep that moves two
    /// hundred prices is a mail nobody reads, and some clients truncate it anyway.
    /// </summary>
    private const int MaxLines = 40;

    private static readonly Dictionary<string, string> Availability = new()
    {
        ["InStock"] = "In stock",
        ["OutOfStock"] = "Out of stock",
        ["PreOrder"] = "Pre-order",
        ["BackOrder"] = "Back-order",
        ["Discontinued"] = "Discontinued",
        ["LimitedAvailability"] = "Limited",
        ["SoldOut"] = "Sold out",
    };

    /// <param name="accountUrl">
    /// Link back to this person's lists, or null when no public base URL is
    /// configured and the API has no request to infer one from.
    /// </param>
    public static EmailMessage Digest(string to, string? accountUrl, IReadOnlyList<ProductChange> changes)
    {
        var shown = changes.Take(MaxLines).ToList();
        var hidden = changes.Count - shown.Count;

        var text = new StringBuilder();
        var html = new StringBuilder();

        html.Append("<div style=\"font-family:system-ui,sans-serif;font-size:15px;color:#191817\">");

        foreach (var change in shown)
        {
            text.AppendLine(change.Name);
            html.Append("<p style=\"margin:0 0 4px\"><a href=\"").Append(Escape(change.Url))
                .Append("\" style=\"color:#1d4fa0;font-weight:600;text-decoration:none\">")
                .Append(Escape(change.Name)).Append("</a><br>");

            foreach (var line in Lines(change))
            {
                text.Append("  ").AppendLine(line);
                html.Append("<span style=\"color:#57544e\">").Append(Escape(line)).Append("</span><br>");
            }

            text.Append("  ").AppendLine(change.Url);
            text.AppendLine();
            html.Append("</p>");
        }

        if (hidden > 0)
        {
            var more = $"…and {hidden} more {(hidden == 1 ? "product" : "products")}.";
            text.AppendLine(more);
            text.AppendLine();
            html.Append("<p style=\"margin:0 0 12px\">").Append(Escape(more)).Append("</p>");
        }

        // The account URL is the only way back into the app — it is the key — so a
        // mail without it leaves the reader nowhere to go but their own bookmark.
        html.Append("<p style=\"margin:16px 0 0;color:#67645d;font-size:13px\">");

        if (accountUrl is not null)
        {
            text.AppendLine("Your lists: " + accountUrl);
            html.Append("<a href=\"").Append(Escape(accountUrl)).Append("\" style=\"color:#1d4fa0\">Your lists</a> · ");
        }

        text.AppendLine("Stop these emails from Menu → Notifications.");
        html.Append("Stop these emails from Menu → Notifications.</p></div>");

        return new EmailMessage(to, Subject(changes), text.ToString(), html.ToString());
    }

    public static EmailMessage Confirmation(string to, string confirmUrl)
    {
        var text = $"""
            Confirm this address to start getting Alza Watchdog price alerts.

            {confirmUrl}

            Nothing else will be sent until you do. If you did not ask for this,
            ignore this email — the address is dropped when the account expires.
            """;

        var html =
            "<div style=\"font-family:system-ui,sans-serif;font-size:15px;color:#191817\">" +
            "<p>Confirm this address to start getting Alza Watchdog price alerts.</p>" +
            $"<p><a href=\"{Escape(confirmUrl)}\" style=\"background:#1d4fa0;color:#fff;padding:10px 16px;" +
            "border-radius:6px;text-decoration:none;display:inline-block\">Confirm address</a></p>" +
            "<p style=\"color:#67645d;font-size:13px\">Nothing else will be sent until you do. " +
            "If you did not ask for this, ignore this email.</p></div>";

        return new EmailMessage(to, "Confirm your Alza Watchdog notifications", text, html);
    }

    /// <summary>The single change reads as its own headline; several are counted.</summary>
    private static string Subject(IReadOnlyList<ProductChange> changes)
    {
        if (changes.Count != 1)
            return $"{changes.Count} changes on your watchlist";

        var only = changes[0];
        var headline = Lines(only).FirstOrDefault() ?? "changed";

        // Long product names get cut so the subject stays readable in a list view.
        var name = only.Name.Length > 60 ? only.Name[..59] + "…" : only.Name;

        return $"{name} — {headline}";
    }

    /// <summary>One line per field that actually moved, in the order the card shows them.</summary>
    internal static IEnumerable<string> Lines(ProductChange change)
    {
        if (change.OldPrice != change.NewPrice)
            yield return Move("Price", change.OldPrice, change.NewPrice, change.Currency);

        if (change.OldCouponPrice != change.NewCouponPrice)
            yield return Move("With code", change.OldCouponPrice, change.NewCouponPrice, change.Currency);

        if (change.OldPlusPrice != change.NewPlusPrice)
            yield return Move("AlzaPlus+", change.OldPlusPrice, change.NewPlusPrice, change.Currency);

        if (change.OldAvailability != change.NewAvailability)
            yield return $"Availability: {Stock(change.OldAvailability)} → {Stock(change.NewAvailability)}";
    }

    private static string Move(string label, decimal? from, decimal? to, string? currency)
    {
        var line = $"{label}: {Money(from, currency)} → {Money(to, currency)}";

        // The difference is the reason the mail was sent, so it is stated rather
        // than left for the reader to work out.
        if (from is not null && to is not null && from != 0)
        {
            var percent = (to.Value - from.Value) / from.Value * 100;
            line += $" ({(to > from ? "+" : "−")}{Math.Abs(percent):0.#} %)";
        }

        return line;
    }

    /// <summary>
    /// Invariant digits with the currency appended. A mail has no idea what locale
    /// the reader's client runs in, and a price that renders differently from the
    /// app is worse than one that is merely plain.
    /// </summary>
    private static string Money(decimal? value, string? currency)
    {
        if (value is null)
            return "no price";

        var amount = value.Value.ToString("#,##0.00", CultureInfo.InvariantCulture);

        return currency switch
        {
            null or "" => amount,
            "EUR" => $"{amount} €",
            _ => $"{amount} {currency}",
        };
    }

    private static string Stock(string? value) =>
        value is null or "" ? "unknown" : Availability.GetValueOrDefault(value, value);

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
