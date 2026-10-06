using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Notifications;

/// <summary>
/// Mails each account one digest of what moved since it was last told, and is run
/// at the end of a sweep rather than on its own timer: the sweep is what creates
/// the changes, so a digest per sweep is a digest per batch of news.
///
/// Each account carries a watermark — the newest snapshot it has already heard
/// about. Nothing is marked as sent until the send succeeds, so a mail server
/// being down delays a digest rather than losing it.
/// </summary>
public class EmailNotificationService(
    AppDbContext db,
    IEmailSender sender,
    IOptions<EmailOptions> options,
    ILogger<EmailNotificationService> logger)
{
    private readonly EmailOptions _options = options.Value;

    /// <returns>How many digests were sent.</returns>
    public async Task<int> SendPendingAsync(CancellationToken ct = default)
    {
        if (!sender.IsEnabled)
            return 0;

        var recipients = await db.Users
            .Where(u => u.Email != null && u.EmailConfirmedAt != null)
            .ToListAsync(ct);

        if (recipients.Count == 0)
            return 0;

        // Read the ceiling once, so a snapshot written while this runs belongs to
        // the next digest instead of being skipped by a watermark that outran it.
        var latestSnapshotId = await db.PriceSnapshots.MaxAsync(s => (long?)s.Id, ct) ?? 0;

        var sent = 0;

        foreach (var user in recipients)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                if (await SendToAsync(user, latestSnapshotId, ct))
                    sent++;
            }
            catch (Exception ex)
            {
                // One unreachable address must not cost everyone else their digest,
                // and leaving the watermark where it is means this account's news
                // is still pending rather than lost.
                logger.LogError(ex, "Could not send the digest for account {UserId}.", user.Id);
            }
        }

        return sent;
    }

    private async Task<bool> SendToAsync(User user, long latestSnapshotId, CancellationToken ct)
    {
        var changes = await ChangesForAsync(user, latestSnapshotId, ct);

        if (changes.Count > 0)
        {
            await sender.SendAsync(
                MailComposer.Digest(user.Email!, AccountUrl(user.Id), changes), ct);
        }

        // Advanced even when there was nothing to say: those snapshots have been
        // considered, and re-examining them every sweep would grow without end.
        user.NotifiedThroughSnapshotId = latestSnapshotId;
        await db.SaveChangesAsync(ct);

        return changes.Count > 0;
    }

    /// <summary>
    /// The net move of each product this account watches, measured from the last
    /// reading it was told about to the newest one. A product that went down and
    /// back up between two digests produces no line, which is the honest answer:
    /// nothing changed since the last mail.
    /// </summary>
    private async Task<IReadOnlyList<ProductChange>> ChangesForAsync(
        User user, long latestSnapshotId, CancellationToken ct)
    {
        var watched = await db.TrackedItems
            .Where(i => i.WatchList.UserId == user.Id)
            .Select(i => i.ProductId)
            .Distinct()
            .ToListAsync(ct);

        if (watched.Count == 0)
            return [];

        var pending = await db.PriceSnapshots
            .Where(s => watched.Contains(s.ProductId)
                        && s.Id > user.NotifiedThroughSnapshotId
                        && s.Id <= latestSnapshotId)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return [];

        var moved = pending.Select(s => s.ProductId).Distinct().ToList();

        // The reading each product was last reported at. Grouping is over ids only
        // — prices are stored as text and must never be aggregated in SQL.
        var baselineIds = await db.PriceSnapshots
            .Where(s => moved.Contains(s.ProductId) && s.Id <= user.NotifiedThroughSnapshotId)
            .GroupBy(s => s.ProductId)
            .Select(g => g.Max(s => s.Id))
            .ToListAsync(ct);

        var baselines = await db.PriceSnapshots
            .Where(s => baselineIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.ProductId, ct);

        var products = await db.Products
            .Where(p => moved.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var changes = new List<ProductChange>();

        foreach (var productId in moved)
        {
            // No earlier reading means this product was added since the last digest.
            // Its first price is not news — it was on screen when it was added.
            if (!baselines.TryGetValue(productId, out var before)
                || !products.TryGetValue(productId, out var product))
                continue;

            var after = pending.Last(s => s.ProductId == productId);

            var change = new ProductChange(
                product.Name ?? product.CanonicalUrl,
                product.CanonicalUrl,
                product.Currency,
                Payable(before, user.HasAlzaPlus),
                Payable(after, user.HasAlzaPlus),
                before.Availability, after.Availability);

            if (MailComposer.Lines(change).Any())
                changes.Add(change);
        }

        return changes;
    }

    /// <summary>
    /// The least this person can pay for a reading — the same rule as the
    /// frontend's <c>payable()</c>. The offers are alternatives, not discounts that
    /// stack, and a members' price is only a price to a member.
    /// </summary>
    private static decimal? Payable(PriceSnapshot snapshot, bool hasAlzaPlus) =>
        new[] { snapshot.Price, snapshot.CouponPrice, hasAlzaPlus ? snapshot.PlusPrice : null }.Min();

    /// <summary>
    /// The bookmarkable link to this account's lists, matching the route the
    /// frontend uses. Null when no public base URL is configured — a link to
    /// "localhost" would be worse than none.
    /// </summary>
    private string? AccountUrl(Guid userId) =>
        string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? null
            : $"{_options.BaseUrl.TrimEnd('/')}/user/{userId:N}";
}
