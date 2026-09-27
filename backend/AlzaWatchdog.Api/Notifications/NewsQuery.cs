using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Api.Notifications;

/// <summary>
/// What the notification bell shows: every product on an account's lists that
/// recorded a new reading since the bell was last opened, one row per product.
///
/// It uses the same watermark idea as the email digest, with a separate mark, so
/// reading the bell does not stop a mail and a mail does not clear the bell.
/// </summary>
public static class NewsQuery
{
    public static async Task<NewsDto> LoadAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var seen = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.SeenThroughSnapshotId)
            .FirstAsync(ct);

        // Read the ceiling once, so a snapshot written while this runs is shown next
        // time, not skipped when the bell is opened and the mark moves up to here.
        var ceiling = await db.PriceSnapshots.MaxAsync(s => (long?)s.Id, ct) ?? 0;

        var items = await db.TrackedItems
            .AsNoTracking()
            .Where(i => i.WatchList.UserId == userId)
            .OrderBy(i => i.WatchList.CreatedAt)
            .ThenBy(i => i.SortOrder)
            .Select(i => new
            {
                i.Id,
                i.WatchListId,
                ListName = i.WatchList.Name,
                i.ProductId,
                i.CreatedAt,
                i.Product.Name,
                i.Product.CanonicalUrl,
                i.Product.Currency,
            })
            .ToListAsync(ct);

        if (items.Count == 0 || ceiling <= seen)
            return new NewsDto(ceiling, []);

        // A product on two lists is one piece of news, reported against the first.
        var byProduct = items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First());

        var watched = byProduct.Keys.ToList();

        // Ids and times only. Prices are stored as text and must never be compared
        // or aggregated in SQL.
        var fresh = await db.PriceSnapshots
            .Where(s => watched.Contains(s.ProductId) && s.Id > seen && s.Id <= ceiling)
            .Select(s => new { s.Id, s.ProductId, s.CapturedAt })
            .ToListAsync(ct);

        // A reading from before the product was on this account's list is not news:
        // that covers the first price, taken when the product is added, and any
        // history that arrives with an import.
        var latestIds = fresh
            .Where(s => s.CapturedAt > items.Where(i => i.ProductId == s.ProductId).Min(i => i.CreatedAt))
            .GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => g.Max(s => s.Id));

        if (latestIds.Count == 0)
            return new NewsDto(ceiling, []);

        var moved = latestIds.Keys.ToList();

        var history = await db.PriceSnapshots
            .AsNoTracking()
            .Where(s => moved.Contains(s.ProductId) && s.Id <= ceiling)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var news = new List<NewsItemDto>();

        foreach (var (productId, latestId) in latestIds)
        {
            var own = history.Where(s => s.ProductId == productId && s.Id <= latestId).ToList();
            var latest = own[^1];
            var previous = own.Count > 1 ? own[^2] : null;
            var item = byProduct[productId];

            news.Add(new NewsItemDto(
                item.Id,
                item.WatchListId,
                item.ListName,
                item.Name,
                item.CanonicalUrl,
                item.Currency,
                ToDto(latest),
                previous is null ? null : ToDto(previous)));
        }

        return new NewsDto(
            ceiling,
            news.OrderByDescending(n => n.Latest.CapturedAt).ToList());
    }

    /// <summary>
    /// Moves the mark up to what the bell showed, never further and never back.
    /// Stopping at what was shown means a reading that arrived while the bell was
    /// open is still new next time.
    /// </summary>
    public static async Task MarkSeenAsync(AppDbContext db, Guid userId, long throughSnapshotId, CancellationToken ct)
    {
        var ceiling = await db.PriceSnapshots.MaxAsync(s => (long?)s.Id, ct) ?? 0;
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

        user.SeenThroughSnapshotId = Math.Max(user.SeenThroughSnapshotId, Math.Min(throughSnapshotId, ceiling));
        await db.SaveChangesAsync(ct);
    }

    private static PriceSnapshotDto ToDto(PriceSnapshot s) =>
        new(s.Price, s.PlusPrice, s.CouponPrice, s.Availability, s.CapturedAt);
}
