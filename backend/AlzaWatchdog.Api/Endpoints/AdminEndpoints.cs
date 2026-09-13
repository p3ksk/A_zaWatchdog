using System.Diagnostics;
using System.Runtime.InteropServices;
using AlzaWatchdog.Api.Admin;
using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // AdminFilter alone: admin rights come from configuration, so these routes
        // keep working when the caller has no account row — the case that matters
        // when restoring into an empty database.
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .AddEndpointFilter<AdminFilter>();

        group.MapGet("/stats", async (AppDbContext db, CancellationToken ct) =>
        {
            var products = await db.Products.AsNoTracking()
                .Select(p => new { p.IsActive })
                .ToListAsync(ct);

            return Results.Ok(new AdminStatsDto(
                Users: await db.Users.CountAsync(ct),
                Lists: await db.WatchLists.CountAsync(ct),
                Items: await db.TrackedItems.CountAsync(ct),
                // Products are stored once now, so this is simply how many rows there are.
                DistinctProducts: products.Count,
                Snapshots: await db.PriceSnapshots.CountAsync(ct),
                InactiveItems: products.Count(p => !p.IsActive)));
        })
        .WithName("AdminStats");

        group.MapGet("/status", (IOptions<DatabaseOptions> database) =>
        {
            using var process = Process.GetCurrentProcess();
            var started = new DateTimeOffset(process.StartTime).ToUniversalTime();

            return Results.Ok(new List<AdminWorkerSettingDto>
            {
                new("Uptime", FormatDuration(DateTimeOffset.UtcNow - started),
                    "How long this API process has been running. Resets on every restart or redeploy."),
                new("Started", started.ToString("yyyy-MM-dd HH:mm 'UTC'"),
                    "When this API process started."),
                new("Memory (working set)", FormatMegabytes(process.WorkingSet64),
                    "Physical memory the process holds right now, as the operating system sees it."),
                new("Memory (GC heap)", FormatMegabytes(GC.GetTotalMemory(forceFullCollection: false)),
                    "Memory currently allocated to .NET objects. The working set is larger because it also counts the runtime, native libraries and memory the GC has not handed back yet."),
                new("Thread pool threads", ThreadPool.ThreadCount.ToString(),
                    "Worker threads in the .NET thread pool. A number that keeps climbing points to blocked or runaway work."),
                new("CPU time", FormatDuration(process.TotalProcessorTime),
                    "Processor time used since start, summed over all cores."),
                new(".NET runtime", RuntimeInformation.FrameworkDescription, null),
                new("Database", database.Value.Provider == DatabaseProvider.MySql ? "MariaDB" : "SQLite", null),
            });
        })
        .WithName("AdminStatus");

        group.MapGet("/workers", async (
            WorkerStatusRegistry workers, AppDbContext db, IOptions<WatchdogOptions> watchdog, CancellationToken ct) =>
        {
            // Mirrors the sweep's own test, window included. A figure counting
            // products the next sweep will not touch would promise work that is
            // not going to happen.
            var due = DateTimeOffset.UtcNow - watchdog.Value.CheckInterval + watchdog.Value.SweepWindow;

            var dueNow = await db.Products.CountAsync(
                p => p.IsActive && (p.LastCheckedAt == null || p.LastCheckedAt < due), ct);
            var paused = await db.Products.CountAsync(p => !p.IsActive, ct);
            var unwatched = await db.Products.CountAsync(p => !p.TrackedBy.Any(), ct);

            // Queue depths are a property of the data, not of the worker, so they
            // are gathered here rather than being stale numbers the worker cached
            // at the end of its last pass.
            var live = new Dictionary<string, List<AdminWorkerSettingDto>>
            {
                [PriceCheckWorker.WorkerName] =
                [
                    new("Products due now", dueNow.ToString(),
                        "Active products whose last check is older than the check interval. This is what the next sweep will fetch, so it should fall to zero after a clean pass."),
                    new("Paused products", paused.ToString(),
                        "Products deactivated after too many failures in a row. They are skipped entirely until someone resumes them."),
                ],
                [AccountCleanupWorker.WorkerName] =
                [
                    new("Products nobody watches", unwatched.ToString(),
                        "Products left on no list at all, usually after the last account tracking them was removed. They are kept for their price history and are not deleted, and are checked on the same schedule as any other product."),
                ],
            };

            return Results.Ok(workers.All().Select(w => new AdminWorkerDto(
                w.Name,
                w.Description,
                w.Enabled,
                w.LastRunAt is null,
                w.LastRunAt,
                w.NextRunAt,
                w.LastOutcome,
                w.Runs,
                [.. w.Settings.Select(s => new AdminWorkerSettingDto(s.Label, s.Value, s.Hint))],
                live.GetValueOrDefault(w.Name) ?? [])));
        })
        .WithName("AdminWorkers");

        group.MapGet("/users", async (
            AppDbContext db, IOptions<AdminOptions> options, CancellationToken ct) =>
        {
            var users = await db.Users
                .AsNoTracking()
                .OrderByDescending(u => u.LastSeenAt)
                .Select(u => new
                {
                    u.Id,
                    u.HasAlzaPlus,
                    u.CreatedAt,
                    u.LastSeenAt,
                    ListCount = u.Lists.Count,
                    ItemCount = u.Lists.Sum(l => l.Items.Count),
                })
                .ToListAsync(ct);

            // Counted once per account: one account can track the same product from
            // several lists, and summing over trackings reported more snapshots
            // than the database holds.
            var snapshotsPerProduct = await db.PriceSnapshots
                .AsNoTracking()
                .GroupBy(s => s.ProductId)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.ProductId, g => g.Count, ct);

            var productsPerUser = (await db.TrackedItems
                    .AsNoTracking()
                    .Select(i => new { i.WatchList.UserId, i.ProductId })
                    .Distinct()
                    .ToListAsync(ct))
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.ProductId).ToList());

            return Results.Ok(users.Select(u => new AdminUserDto(
                u.Id, u.HasAlzaPlus, options.Value.IsAdmin(u.Id),
                u.ListCount, u.ItemCount,
                productsPerUser.GetValueOrDefault(u.Id, []).Sum(id => snapshotsPerProduct.GetValueOrDefault(id)),
                u.CreatedAt, u.LastSeenAt)));
        })
        .WithName("AdminUsers");

        group.MapGet("/items", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.TrackedItems
                .AsNoTracking()
                .Include(i => i.WatchList)
                .Include(i => i.Product)
                .OrderBy(i => i.Product.ProductCode)
                .ToListAsync(ct);

            var snapshots = await LoadSnapshotsAsync(db, items.Select(i => i.ProductId).Distinct().ToList(), ct);

            return Results.Ok(items.Select(i => new AdminItemDto(
                i.Id,
                i.WatchList.UserId,
                i.Product.ProductCode,
                i.Product.CanonicalUrl,
                i.Product.Name,
                i.Product.Currency,
                i.Product.LastPrice,
                i.Product.LastPlusPrice,
                i.Product.LastCouponPrice,
                i.Product.LastCheckedAt,
                i.Product.LastError,
                i.Product.ConsecutiveFailures,
                i.Product.IsActive,
                snapshots.GetValueOrDefault(i.ProductId) ?? [])));
        })
        .WithName("AdminItems");
    }

    /// <summary>"3h 30m", "2d 4h", "45s" — the two largest units are enough to read at a glance.</summary>
    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1)
            return $"{span.Hours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1)
            return $"{span.Minutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / (1024 * 1024)} MB";

    private static async Task<Dictionary<Guid, List<PriceSnapshotDto>>> LoadSnapshotsAsync(
        AppDbContext db, List<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0)
            return [];

        var rows = await db.PriceSnapshots
            .AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(ct);

        return rows
            .GroupBy(s => s.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => new PriceSnapshotDto(
                    s.Price, s.PlusPrice, s.CouponPrice, s.Availability, s.CapturedAt)).ToList());
    }
}
