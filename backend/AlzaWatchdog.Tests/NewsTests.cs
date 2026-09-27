using AlzaWatchdog.Api.Data;
using AlzaWatchdog.Api.Domain;
using AlzaWatchdog.Api.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlzaWatchdog.Tests;

/// <summary>
/// What the notification bell shows. Several readings for one product collapse
/// into the newest, and a product's first price, taken when it was added, is not
/// news to the person who just added it.
/// </summary>
public class NewsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteAppDbContext _db;

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

    public NewsTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _db = new SqliteAppDbContext(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    private User AddUser()
    {
        var user = new User { Id = Guid.NewGuid(), CreatedAt = Now, LastSeenAt = Now };

        _db.Users.Add(user);
        _db.WatchLists.Add(new WatchList { Id = Guid.NewGuid(), UserId = user.Id, Name = "List", CreatedAt = Now });
        _db.SaveChanges();

        return user;
    }

    private WatchList AddList(User user, string name)
    {
        var list = new WatchList { Id = Guid.NewGuid(), UserId = user.Id, Name = name, CreatedAt = Now.AddSeconds(1) };
        _db.WatchLists.Add(list);
        _db.SaveChanges();
        return list;
    }

    private Product AddProduct(User user, string name = "CUDY N300")
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            ProductCode = Random.Shared.Next(1_000_000, 9_999_999).ToString(),
            CanonicalUrl = "https://www.alza.sk/cudy-n300-wifi-router-d10818009.htm",
            Name = name,
            Currency = "EUR",
            CreatedAt = Now,
        };

        _db.Products.Add(product);
        Track(product, _db.WatchLists.First(l => l.UserId == user.Id));

        return product;
    }

    private void Track(Product product, WatchList list)
    {
        _db.TrackedItems.Add(new TrackedItem
        {
            Id = Guid.NewGuid(),
            WatchListId = list.Id,
            ProductId = product.Id,
            CreatedAt = Now,
        });
        _db.SaveChanges();
    }

    private long AddSnapshot(Product product, decimal? price, int minutesLater, string availability = "InStock")
    {
        var snapshot = new PriceSnapshot
        {
            ProductId = product.Id,
            Price = price,
            Availability = availability,
            CapturedAt = Now.AddMinutes(minutesLater),
        };

        _db.PriceSnapshots.Add(snapshot);
        _db.SaveChanges();

        return snapshot.Id;
    }

    [Fact]
    public async Task Shows_only_the_newest_reading_of_a_product_with_the_one_before_it()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m, minutesLater: 0);
        AddSnapshot(product, 18.90m, minutesLater: 60);
        var newest = AddSnapshot(product, 17.90m, minutesLater: 120);

        var news = await NewsQuery.LoadAsync(_db, user.Id, default);

        var item = Assert.Single(news.Items);
        Assert.Equal(17.90m, item.Latest.Price);
        Assert.Equal(18.90m, item.Previous?.Price);
        Assert.Equal(newest, news.ThroughSnapshotId);
    }

    [Fact]
    public async Task The_first_price_taken_when_a_product_is_added_is_not_news()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m, minutesLater: 0);

        var news = await NewsQuery.LoadAsync(_db, user.Id, default);

        Assert.Empty(news.Items);
    }

    [Fact]
    public async Task Readings_already_seen_are_not_shown_again()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m, minutesLater: 60);

        var first = await NewsQuery.LoadAsync(_db, user.Id, default);
        await NewsQuery.MarkSeenAsync(_db, user.Id, first.ThroughSnapshotId, default);

        Assert.Single(first.Items);
        Assert.Empty((await NewsQuery.LoadAsync(_db, user.Id, default)).Items);
    }

    [Fact]
    public async Task A_reading_that_arrives_while_the_bell_is_open_stays_new()
    {
        var user = AddUser();
        var product = AddProduct(user);
        AddSnapshot(product, 19.90m, minutesLater: 60);

        var shown = await NewsQuery.LoadAsync(_db, user.Id, default);
        AddSnapshot(product, 17.90m, minutesLater: 120);
        await NewsQuery.MarkSeenAsync(_db, user.Id, shown.ThroughSnapshotId, default);

        var item = Assert.Single((await NewsQuery.LoadAsync(_db, user.Id, default)).Items);
        Assert.Equal(17.90m, item.Latest.Price);
    }

    [Fact]
    public async Task Marking_seen_never_moves_the_mark_back()
    {
        var user = AddUser();
        var product = AddProduct(user);
        var id = AddSnapshot(product, 19.90m, minutesLater: 60);

        await NewsQuery.MarkSeenAsync(_db, user.Id, id, default);
        await NewsQuery.MarkSeenAsync(_db, user.Id, 0, default);

        Assert.Equal(id, _db.Users.AsNoTracking().Single(u => u.Id == user.Id).SeenThroughSnapshotId);
    }

    [Fact]
    public async Task A_product_on_two_lists_is_one_piece_of_news()
    {
        var user = AddUser();
        var product = AddProduct(user);
        Track(product, AddList(user, "Second"));
        AddSnapshot(product, 19.90m, minutesLater: 60);

        var item = Assert.Single((await NewsQuery.LoadAsync(_db, user.Id, default)).Items);
        Assert.Equal("List", item.ListName);
    }

    [Fact]
    public async Task Another_accounts_products_are_not_news()
    {
        var user = AddUser();
        var other = AddUser();
        AddSnapshot(AddProduct(other), 19.90m, minutesLater: 60);

        Assert.Empty((await NewsQuery.LoadAsync(_db, user.Id, default)).Items);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
