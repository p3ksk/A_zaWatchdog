using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using AlzaWatchdog.Api.Admin;
using AlzaWatchdog.Api.Scraping;
using AlzaWatchdog.Api.Workers;
using AlzaWatchdog.Api.Auth;
using AlzaWatchdog.Api.Contracts;
using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Api.Endpoints;

public static class UserEndpoints
{
    public const string DefaultListName = "My watchlist";

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Account");

        group.MapPost("/", async (AppDbContext db, IOptions<AdminOptions> admin, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var user = new User { Id = Guid.NewGuid(), CreatedAt = now, LastSeenAt = now };

            // Every account starts with one list, so the app always has somewhere
            // to send the browser rather than an empty "pick a list" state.
            var list = new WatchList
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = DefaultListName,
                CreatedAt = now,
            };

            db.Users.Add(user);
            db.WatchLists.Add(list);
            await db.SaveChangesAsync(ct);

            return Results.Ok(Describe(
                user, admin.Value, [new WatchListDto(list.Id, list.Name, list.CreatedAt, 0)]));
        })
        .WithName("CreateAccount")
        .WithSummary("Issues a new account key and its first list.");

        group.MapPost("/start", async (
            AddItemRequest request,
            AppDbContext db,
            IAlzaScraper scraper,
            PriceUpdateService updater,
            IOptions<AdminOptions> admin,
            IOptions<WatchdogOptions> watchdog,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            if (!AlzaUrl.TryParse(request.Url, out var parsed))
            {
                return Results.Problem(
                    title: "Not a valid alza.sk product URL",
                    detail: "Expected something like https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var now = DateTimeOffset.UtcNow;

            var product = await db.Products
                .FirstOrDefaultAsync(p => p.ProductCode == parsed.ProductCode, ct);

            if (product is null)
            {
                // One retry, as when adding to an existing list: this is the first
                // thing a new account ever does, and a challenge here means someone
                // is turned away before they have an account at all.
                var result = await ChallengeRetry.FetchAsync(
                    scraper, parsed.CanonicalUrl, watchdog.Value.InteractiveChallengeRetryDelay,
                    loggers.CreateLogger(typeof(ChallengeRetry)), ct);

                if (result.Status == ScrapeStatus.ProductNotFound)
                {
                    return Results.Problem(
                        title: "No such product",
                        detail: "alza.sk returned 404 for this URL.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                 product = new Product
                {
                    Id = Guid.NewGuid(),
                    ProductCode = parsed.ProductCode,
                    CanonicalUrl = parsed.CanonicalUrl,
                    CreatedAt = now,
                };

                db.Products.Add(product);
                updater.Apply(db, product, result, now);
            }

            // The account is only built once the product is not known to be fake.
            // Everything here lands in one SaveChanges, so a URL that turns out to
            // be a 404 leaves no half-made account behind. That is
            // the whole point of not creating one when the page is merely opened.
            var user = new User { Id = Guid.NewGuid(), CreatedAt = now, LastSeenAt = now };
            var list = new WatchList
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = DefaultListName,
                CreatedAt = now,
            };

            db.Users.Add(user);
            db.WatchLists.Add(list);
            db.TrackedItems.Add(new TrackedItem
            {
                Id = Guid.NewGuid(),
                WatchListId = list.Id,
                ProductId = product.Id,
                SortOrder = 0,
                CreatedAt = now,
            });

            await db.SaveChangesAsync(ct);

            return Results.Ok(Describe(
                user, admin.Value, [new WatchListDto(list.Id, list.Name, list.CreatedAt, 1)]));
        })
        .WithName("StartWithFirstProduct")
        .WithSummary("Creates an account, its first list and its first product together.");

        // Lets someone re-enter a key from another device and get their lists back.
        group.MapGet("/{userId:guid}", async (
            Guid userId, AppDbContext db, IOptions<AdminOptions> admin, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Results.NotFound();

            // Presenting the key is access, and the cleanup worker deletes accounts
            // by how long they have gone untouched. Without this, simply opening a
            // bookmark would not count as using the account.
            user.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var lists = await ListEndpoints.LoadListsAsync(db, userId, ct);
            return Results.Ok(Describe(user, admin.Value, lists));
        })
        .WithName("GetAccount")
        .WithSummary("Validates an account key and returns its lists.");

        group.MapPatch("/", async (
            UpdateAccountRequest request,
            HttpContext http,
            AppDbContext db,
            IOptions<AdminOptions> admin,
            CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);

            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            user.HasAlzaPlus = request.HasAlzaPlus;
            await db.SaveChangesAsync(ct);

            var lists = await ListEndpoints.LoadListsAsync(db, userId, ct);
            return Results.Ok(Describe(user, admin.Value, lists));
        })
        .AddEndpointFilter<UserTokenFilter>()
        .WithName("UpdateAccount")
        .WithSummary("Records whether this account holds an AlzaPlus+ membership.");

        MapEmailEndpoints(group);
    }

    /// <summary>
    /// Setting, clearing and confirming the address price changes are mailed to.
    ///
    /// An address is only believed once its owner has followed a link sent to it.
    /// The person typing it into the box holds the account key, which says nothing
    /// about whether they own the mailbox — without the round trip the app is a
    /// button for mailing strangers.
    /// </summary>
    private static void MapEmailEndpoints(RouteGroupBuilder group)
    {
        group.MapPut("/email", async (
            SetEmailRequest request,
            HttpContext http,
            AppDbContext db,
            IEmailSender sender,
            IOptions<EmailOptions> email,
            IOptions<AdminOptions> admin,
            CancellationToken ct) =>
        {
            if (!sender.IsEnabled)
            {
                return Results.Problem(
                    title: "Email notifications are not available",
                    detail: "This server has no mail account configured to send them from.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            var address = request.Email.Trim();

            if (!IsPlausibleAddress(address))
            {
                return Results.Problem(
                    title: "That does not look like an email address",
                    detail: "Expected something like name@example.com",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var userId = UserTokenFilter.GetUserId(http);
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

            // Re-saving the same confirmed address re-sends the link rather than
            // quietly doing nothing: the reason to do it is a mail that never arrived.
            user.Email = address;
            user.EmailConfirmedAt = null;
            user.EmailConfirmToken = NewToken();
            await db.SaveChangesAsync(ct);

            var link = EmailLinks.Confirm(EmailLinks.PublicBase(email.Value, http.Request), user.EmailConfirmToken);
            await sender.SendAsync(MailComposer.Confirmation(address, link), ct);

            var lists = await ListEndpoints.LoadListsAsync(db, userId, ct);
            return Results.Ok(Describe(user, admin.Value, lists));
        })
        .AddEndpointFilter<UserTokenFilter>()
        .WithName("SetAccountEmail")
        .WithSummary("Saves an address and mails it a confirmation link.");

        group.MapDelete("/email", async (
            HttpContext http,
            AppDbContext db,
            IOptions<AdminOptions> admin,
            CancellationToken ct) =>
        {
            var userId = UserTokenFilter.GetUserId(http);
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);

            user.Email = null;
            user.EmailConfirmedAt = null;
            user.EmailConfirmToken = null;
            await db.SaveChangesAsync(ct);

            var lists = await ListEndpoints.LoadListsAsync(db, userId, ct);
            return Results.Ok(Describe(user, admin.Value, lists));
        })
        .AddEndpointFilter<UserTokenFilter>()
        .WithName("ClearAccountEmail")
        .WithSummary("Stops notifications and forgets the address.");

        // Followed from a mail client, so it answers with a page rather than JSON,
        // and carries no account key: the token in the link is the whole proof.
        group.MapGet("/email/confirm/{token}", async (
            string token,
            HttpContext http,
            AppDbContext db,
            IOptions<EmailOptions> email,
            CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.EmailConfirmToken == token, ct);

            if (user is null)
            {
                // Also the second click on the same link, since the token is cleared
                // by the first — hence a page that does not read as an error.
                return ConfirmationPage(
                    "Nothing to confirm",
                    "This link has already been used, or the address has since been changed.",
                    null);
            }

            user.EmailConfirmedAt = DateTimeOffset.UtcNow;
            user.EmailConfirmToken = null;

            // Notifications begin now. Without this the first digest would carry
            // every change recorded since the account was created.
            user.NotifiedThroughSnapshotId = await db.PriceSnapshots.MaxAsync(s => (long?)s.Id, ct) ?? 0;

            await db.SaveChangesAsync(ct);

            var home = $"{EmailLinks.PublicBase(email.Value, http.Request)}/user/{user.Id:N}";

            return ConfirmationPage(
                "Address confirmed",
                "You will get an email when a price or availability changes on your lists.",
                home);
        })
        .WithName("ConfirmAccountEmail")
        .WithSummary("Confirms an address from the link that was mailed to it.");
    }

    private static AccountDto Describe(User user, AdminOptions admin, IReadOnlyList<WatchListDto> lists) =>
        new(user.Id, user.HasAlzaPlus, admin.IsAdmin(user.Id), user.Email, user.EmailConfirmedAt is not null, lists);

    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>
    /// Enough to catch a typo, and no more. Whether an address exists is settled by
    /// the confirmation mail arriving, not by a pattern.
    /// </summary>
    private static bool IsPlausibleAddress(string value) =>
        value.Length is > 2 and <= 320
        && MailAddress.TryCreate(value, out var parsed)
        && parsed.Host.Contains('.');

    /// <summary>
    /// A whole page, because this opens in whatever browser the mail client hands
    /// it to — with no app loaded around it and nothing else on screen to explain
    /// what just happened.
    /// </summary>
    private static IResult ConfirmationPage(string title, string detail, string? homeUrl)
    {
        var link = homeUrl is null
            ? ""
            : $"""<p><a href="{WebUtility.HtmlEncode(homeUrl)}">Back to your lists</a></p>""";

        return Results.Content($"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{WebUtility.HtmlEncode(title)}</title></head>
            <body style="font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1rem;color:#191817">
            <h1 style="font-size:1.4rem">{WebUtility.HtmlEncode(title)}</h1>
            <p>{WebUtility.HtmlEncode(detail)}</p>
            {link}
            </body></html>
            """, "text/html");
    }
}
