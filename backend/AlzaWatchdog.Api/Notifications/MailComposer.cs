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
        var subject = Subject(changes);

        var text = new StringBuilder();
        var rows = new StringBuilder();

        foreach (var change in shown)
        {
            text.AppendLine(change.Name);

            rows.Append($"""<tr><td style="padding:14px 24px;border-top:1px solid {Rule}">""")
                .Append($"""<a href="{Escape(change.Url)}" style="color:{Ink};font-weight:600;font-size:15px;text-decoration:none">""")
                .Append(Escape(change.Name)).Append("</a>")
                .Append("""<table role="presentation" cellpadding="0" cellspacing="0" style="margin-top:6px;border-collapse:collapse">""");

            foreach (var move in Moves(change))
            {
                text.Append("  ").AppendLine(move.ToString());
                rows.Append(MoveRow(move));
            }

            rows.Append("</table></td></tr>");

            text.Append("  ").AppendLine(change.Url);
            text.AppendLine();
        }

        if (hidden > 0)
        {
            var more = $"…and {hidden} more {(hidden == 1 ? "product" : "products")}.";
            text.AppendLine(more);
            text.AppendLine();
            rows.Append($"""<tr><td style="padding:14px 24px;border-top:1px solid {Rule};color:{InkDim};font-size:13px">{Escape(more)}</td></tr>""");
        }

        // The account URL is the only way back into the app — it is the key — so a
        // mail without it leaves the reader nowhere to go but their own bookmark.
        var footer = new StringBuilder();

        if (accountUrl is not null)
        {
            text.AppendLine("Your lists: " + accountUrl);
            footer.Append($"""<a href="{Escape(accountUrl)}" style="color:{Accent};text-decoration:none">Your lists</a> · """);
        }

        text.AppendLine("Stop these emails from Menu → Notifications.");
        footer.Append("Stop these emails from Menu → Notifications.");

        var html = Layout(
            subject,
            changes.Count == 1 ? "1 change on your watchlist" : $"{changes.Count} changes on your watchlist",
            rows.ToString(),
            footer.ToString());

        return new EmailMessage(to, subject, text.ToString(), html);
    }

    public static EmailMessage Confirmation(string to, string confirmUrl)
    {
        const string subject = "Confirm your Alza Watchdog notifications";

        var text = $"""
            Confirm this address to start getting Alza Watchdog price alerts.

            {confirmUrl}

            Nothing else will be sent until you do. If you did not ask for this,
            ignore this email — the address is dropped when the account expires.
            """;

        var body = $"""
            <tr><td style="padding:18px 24px 22px;border-top:1px solid {Rule}">
            <p style="margin:0 0 18px;font-size:15px;line-height:1.5">Confirm this address to start getting Alza Watchdog price alerts.</p>
            <a href="{Escape(confirmUrl)}" style="display:inline-block;background:{Accent};color:#ffffff;font-weight:500;font-size:14px;padding:10px 18px;border-radius:3px;text-decoration:none">Confirm address</a>
            </td></tr>
            """;

        var html = Layout(
            subject,
            "Confirm your address",
            body,
            "Nothing else will be sent until you do. If you did not ask for this, ignore this email.");

        return new EmailMessage(to, subject, text, html);
    }

    /// <summary>
    /// The page the confirmation link lands on, in the same frame as the mail that
    /// carried it, so the click reads as one step rather than a jump to somewhere else.
    /// </summary>
    public static string ConfirmationPage(string title, string detail, string? homeUrl)
    {
        var link = homeUrl is null
            ? ""
            : $"""<a href="{Escape(homeUrl)}" style="display:inline-block;margin-top:18px;background:{Accent};color:#ffffff;font-weight:500;font-size:14px;padding:10px 18px;border-radius:3px;text-decoration:none">Back to your lists</a>""";

        var body = $"""
            <tr><td style="padding:18px 24px 22px;border-top:1px solid {Rule}">
            <p style="margin:0;font-size:15px;line-height:1.5">{Escape(detail)}</p>
            {link}
            </td></tr>
            """;

        return Layout(title, title, body, null);
    }

    // The app's light "Ledger" palette. Mail clients cannot be relied on for dark
    // mode or custom properties, so the values are written out inline.
    private const string Paper = "#e8eef8";
    private const string Chrome = "#f4f7fc";
    private const string Panel = "#ffffff";
    private const string Rule = "#d5dff0";
    private const string Ink = "#191817";
    private const string InkMid = "#57544e";
    private const string InkDim = "#67645d";
    private const string Accent = "#1d4fa0";
    private const string Sans = "'IBM Plex Sans',system-ui,-apple-system,'Segoe UI',Roboto,sans-serif";
    private const string Mono = "'IBM Plex Mono',ui-monospace,SFMono-Regular,Menlo,Consolas,monospace";

    /// <summary>
    /// The frame every mail shares, mirroring the app: the wordmark on the chrome
    /// strip, an accent-barred heading, rows ruled by hairlines. Tables and inline
    /// styles only, since that is all mail clients agree on.
    /// </summary>
    private static string Layout(string title, string heading, string rows, string? footer) => $"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta name="color-scheme" content="light only">
        <title>{Escape(title)}</title></head>
        <body style="margin:0;padding:0;background:{Paper}">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Paper};border-collapse:collapse">
        <tr><td align="center" style="padding:24px 12px">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:{Panel};border:1px solid {Rule};border-radius:3px;border-collapse:separate;font-family:{Sans};color:{Ink}">
        <tr><td style="background:{Chrome};padding:15px 24px;border-bottom:1px solid {Rule};border-radius:3px 3px 0 0;font-size:18px;font-weight:600;letter-spacing:-0.01em;color:{Ink}">A<span style="color:{Accent}">*</span>za_Watchdog</td></tr>
        <tr><td style="padding:20px 24px 14px"><div style="border-left:4px solid {Accent};padding-left:12px;font-size:20px;font-weight:600;color:{Ink}">{Escape(heading)}</div></td></tr>
        {rows}
        {(footer is null ? "" : $"""<tr><td style="background:{Chrome};padding:14px 24px;border-top:1px solid {Rule};border-radius:0 0 3px 3px;font-size:12px;line-height:1.5;color:{InkDim}">{footer}</td></tr>""")}
        </table>
        </td></tr>
        </table>
        </body></html>
        """;

    /// <summary>
    /// Label, old → new in the mono face the app uses for every figure, and the
    /// difference. A drop is the good news the app marks in its accent colour.
    /// </summary>
    private static string MoveRow(Move move)
    {
        var cell = "padding:2px 0;font-size:13px;vertical-align:baseline";
        var toColour = move.Better ? Accent : Ink;

        var delta = move.Delta is null
            ? ""
            : $"""<span style="margin-left:8px;color:{(move.Better ? Accent : InkDim)}">{Escape(move.Delta)}</span>""";

        return $"""
            <tr><td width="92" style="{cell};width:92px;padding-right:12px;color:{InkDim};white-space:nowrap">{Escape(move.Label)}</td>
            <td style="{cell};font-family:{Mono};color:{InkMid}">{Escape(move.From)} <span style="color:{InkDim}">→</span> <span style="color:{toColour};font-weight:600">{Escape(move.To)}</span>{delta}</td></tr>
            """;
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
    internal static IEnumerable<string> Lines(ProductChange change) =>
        Moves(change).Select(m => m.ToString());

    /// <param name="Better">A lower price or a move into stock: the news worth highlighting.</param>
    private sealed record Move(string Label, string From, string To, string? Delta, bool Better)
    {
        public override string ToString() =>
            Delta is null ? $"{Label}: {From} → {To}" : $"{Label}: {From} → {To} ({Delta})";
    }

    private static IEnumerable<Move> Moves(ProductChange change)
    {
        if (change.OldPrice != change.NewPrice)
            yield return PriceMove("Price", change.OldPrice, change.NewPrice, change.Currency);

        if (change.OldCouponPrice != change.NewCouponPrice)
            yield return PriceMove("With code", change.OldCouponPrice, change.NewCouponPrice, change.Currency);

        if (change.OldPlusPrice != change.NewPlusPrice)
            yield return PriceMove("AlzaPlus+", change.OldPlusPrice, change.NewPlusPrice, change.Currency);

        if (change.OldAvailability != change.NewAvailability)
            yield return new Move(
                "Availability",
                Stock(change.OldAvailability),
                Stock(change.NewAvailability),
                null,
                change.NewAvailability == "InStock");
    }

    private static Move PriceMove(string label, decimal? from, decimal? to, string? currency)
    {
        // The difference is the reason the mail was sent, so it is stated rather
        // than left for the reader to work out.
        string? delta = null;

        if (from is not null && to is not null && from != 0)
        {
            var percent = (to.Value - from.Value) / from.Value * 100;
            delta = $"{(to > from ? "+" : "−")}{Math.Abs(percent):0.#} %";
        }

        return new Move(label, Money(from, currency), Money(to, currency), delta, to < from);
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
