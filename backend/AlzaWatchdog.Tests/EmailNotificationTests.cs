using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AlzaWatchdog.Tests;

/// <summary>
/// What each account is told after a sweep.
///
/// The rules worth pinning down are the ones about silence: an unconfirmed
/// address, a product someone has only just added, and a send that failed must
/// all leave the account's place in the queue where it was.
/// </summary>
public class EmailNotificationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteAppDbContext _db;
    private readonly FakeSender _sender = new();

    private static readonly DateTimeOffset Now = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly EmailOptions _options = new()
    {
        Host = "smtp.example.com",
        From = "watchdog@example.com",
        BaseUrl = "https://watchdog.example.com",
    };

    public EmailNotificationTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    private EmailNotificationService Service() =>
        new(_db, _sender, Options.Create(_options), NullLogger<EmailNotificationService>.Instance);

    private User AddUser(bool confirmed = true, string? email = "watcher@example.com", bool hasPlus = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            CreatedAt = Now,
            LastSeenAt = Now,
            HasAlzaPlus = hasPlus,
            Email = email,
            EmailConfirmedAt = confirmed && email is not null ? Now : null,
        };

        _db.Users.Add(user);
        _db.WatchLists.Add(new WatchList { Id = Guid.NewGuid(), UserId = user.Id, Name = "List", CreatedAt = Now });
        _db.SaveChanges();

        return user;
    }

    private Product AddProduct(User user, string name = "CUDY N300", string currency = "EUR")
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            ProductCode = Random.Shared.Next(1_000_000, 9_999_999).ToString(),
            CanonicalUrl = "https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm",
            Name = name,
            Currency = currency,
            CreatedAt = Now,
        };

        _db.Products.Add(product);
        _db.TrackedItems.Add(new TrackedItem
        {
            Id = Guid.NewGuid(),
            WatchListId = _db.WatchLists.First(l => l.UserId == user.Id).Id,
            ProductId = product.Id,
            CreatedAt = Now,
        });
        _db.SaveChanges();

        return product;
    }

    /// <summary>Appends a reading and returns its id, which is what watermarks are measured in.</summary>
    private long AddSnapshot(
        Product product, decimal? price, string availability = "InStock",
        decimal? plusPrice = null, int minutesLater = 0)
    {
        var snapshot = new PriceSnapshot
        {
            ProductId = product.Id,
            Price = price,
            PlusPrice = plusPrice,
            Availability = availability,
            CapturedAt = Now.AddMinutes(minutesLater),
        };

        _db.PriceSnapshots.Add(snapshot);
        _db.SaveChanges();

        return snapshot.Id;
    }

    /// <summary>Puts the account level with everything recorded so far, as confirming it does.</summary>
    private void MarkCaughtUp(User user)
    {
        user.NotifiedThroughSnapshotId = _db.PriceSnapshots.Max(s => (long?)s.Id) ?? 0;
        _db.SaveChanges();
    }

    [Fact]
    public async Task Mails_a_price_move_on_a_watched_product()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m);
        MarkCaughtUp(user);
        AddSnapshot(product, 17.90m, minutesLater: 60);

        var sent = await Service().SendPendingAsync();

        Assert.Equal(1, sent);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("watcher@example.com", mail.To);
        Assert.Contains("19.90 € → 17.90 €", mail.TextBody);
        Assert.Contains("CUDY N300", mail.Subject);
    }

    [Fact]
    public async Task Moves_the_watermark_so_one_change_is_only_mailed_once()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m);
        MarkCaughtUp(user);
        var moved = AddSnapshot(product, 17.90m, minutesLater: 60);

        await Service().SendPendingAsync();
        await Service().SendPendingAsync();

        Assert.Single(_sender.Sent);
        Assert.Equal(moved, _db.Users.Single().NotifiedThroughSnapshotId);
    }

    [Fact]
    public async Task Says_nothing_to_an_address_that_was_never_confirmed()
    {
        var user = AddUser(confirmed: false);
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m);
        MarkCaughtUp(user);
        AddSnapshot(product, 17.90m, minutesLater: 60);

        var sent = await Service().SendPendingAsync();

        Assert.Equal(0, sent);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Does_not_announce_the_first_reading_of_a_newly_added_product()
    {
        var user = AddUser();
        MarkCaughtUp(user);

        // Added after the account was caught up: its opening price was on screen
        // when it was added, so it is not news.
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m, minutesLater: 30);

        var sent = await Service().SendPendingAsync();

        Assert.Equal(0, sent);
        Assert.Empty(_sender.Sent);

        // …but the next move on that product is.
        AddSnapshot(product, 18.90m, minutesLater: 90);
        Assert.Equal(1, await Service().SendPendingAsync());
    }

    [Fact]
    public async Task Reports_the_net_move_across_several_readings_as_one_line()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 20.00m);
        MarkCaughtUp(user);
        AddSnapshot(product, 19.00m, minutesLater: 60);
        AddSnapshot(product, 18.00m, minutesLater: 120);

        await Service().SendPendingAsync();

        var mail = Assert.Single(_sender.Sent);
        Assert.Contains("20.00 € → 18.00 €", mail.TextBody);
        Assert.DoesNotContain("19.00 €", mail.TextBody);
    }

    [Fact]
    public async Task Keeps_the_news_pending_when_the_send_fails()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m);
        MarkCaughtUp(user);
        var moved = AddSnapshot(product, 17.90m, minutesLater: 60);
        _sender.FailNext = true;

        Assert.Equal(0, await Service().SendPendingAsync());
        Assert.NotEqual(moved, _db.Users.Single().NotifiedThroughSnapshotId);

        // The next sweep sends what the failure held back.
        Assert.Equal(1, await Service().SendPendingAsync());
        Assert.Equal(moved, _db.Users.Single().NotifiedThroughSnapshotId);
    }

    [Fact]
    public async Task Ignores_a_members_price_for_an_account_without_the_membership()
    {
        var user = AddUser(hasPlus: false);
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m, plusPrice: 18.90m);
        MarkCaughtUp(user);
        AddSnapshot(product, 19.90m, plusPrice: 16.90m, minutesLater: 60);

        Assert.Equal(0, await Service().SendPendingAsync());

        // The same move reaches a member.
        var member = AddUser(hasPlus: true, email: "member@example.com");
        AddProduct(member);
        var theirs = _db.Products.Single(p => p.Id != product.Id);
        AddSnapshot(theirs, 19.90m, plusPrice: 18.90m);
        MarkCaughtUp(member);
        AddSnapshot(theirs, 19.90m, plusPrice: 16.90m, minutesLater: 60);

        await Service().SendPendingAsync();

        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("member@example.com", mail.To);
        Assert.Contains("AlzaPlus+: 18.90 € → 16.90 €", mail.TextBody);
    }

    [Fact]
    public async Task Reports_a_product_alza_stopped_selling()
    {
        var user = AddUser();
        var product = AddProduct(user, name: "Lenovo ThinkPad E14");
        AddSnapshot(product, 1309m);
        MarkCaughtUp(user);
        AddSnapshot(product, null, availability: "Discontinued", minutesLater: 60);

        await Service().SendPendingAsync();

        var mail = Assert.Single(_sender.Sent);
        Assert.Contains("1,309.00 € → no price", mail.TextBody);
        Assert.Contains("Availability: In stock → Discontinued", mail.TextBody);
    }

    [Fact]
    public async Task Sends_nothing_when_the_server_has_no_mail_account()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m);
        MarkCaughtUp(user);
        AddSnapshot(product, 17.90m, minutesLater: 60);

        // What an unconfigured SmtpEmailSender reports.
        _sender.IsEnabled = false;

        Assert.Equal(0, await Service().SendPendingAsync());
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public void Digest_counts_the_products_when_there_is_more_than_one()
    {
        var changes = new[]
        {
            new ProductChange("A", "https://a", "EUR", 10m, 9m, null, null, null, null, "InStock", "InStock"),
            new ProductChange("B", "https://b", "EUR", 20m, 25m, null, null, null, null, "InStock", "OutOfStock"),
        };

        var mail = MailComposer.Digest("to@example.com", "https://watchdog.example.com/user/abc", changes);

        Assert.Equal("2 changes on your watchlist", mail.Subject);
        Assert.Contains("(−10 %)", mail.TextBody);
        Assert.Contains("(+25 %)", mail.TextBody);
        Assert.Contains("https://watchdog.example.com/user/abc", mail.TextBody);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    /// <summary>Captures what would have been sent, and can refuse once on demand.</summary>
    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool FailNext { get; set; }

        public bool IsEnabled { get; set; } = true;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("smtp is down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
