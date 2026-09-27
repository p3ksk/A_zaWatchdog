namespace AlzaWatchdog.Api.Contracts;

public record AccountDto(
    Guid UserId,
    bool HasAlzaPlus,
    bool IsAdmin,
    /// <summary>
    /// Whether an address is on file, confirmed or not. The address itself is never
    /// sent back: the account key is the only credential, and whoever holds a leaked
    /// link should not learn the owner's mailbox from it.
    /// </summary>
    bool HasEmail,
    /// <summary>Mail only starts once the emailed link has been followed.</summary>
    bool EmailConfirmed,
    IReadOnlyList<WatchListDto> Lists);

public record UpdateAccountRequest(bool HasAlzaPlus);

public record SetEmailRequest(string Email);

public record WatchListDto(Guid Id, string Name, DateTimeOffset CreatedAt, int ItemCount);

public record SaveListRequest(string Name);

public record AddItemRequest(string Url);

public record MoveItemRequest(Guid TargetListId);

/// <summary>Every tracked item in the new manual order, oldest position first.</summary>
public record ReorderItemsRequest(IReadOnlyList<Guid> OrderedItemIds);

public record PriceSnapshotDto(
    decimal? Price,
    decimal? PlusPrice,
    decimal? CouponPrice,
    string? Availability,
    DateTimeOffset CapturedAt);

/// <summary>
/// One product that recorded a new reading since the bell was last opened. Only the
/// newest reading is given, plus the one before it so the change can be shown.
/// </summary>
public record NewsItemDto(
    Guid ItemId,
    Guid ListId,
    string ListName,
    string? Name,
    string Url,
    string? Currency,
    PriceSnapshotDto Latest,
    PriceSnapshotDto? Previous);

/// <param name="ThroughSnapshotId">Sent back when the bell is opened, to mark exactly this much as seen.</param>
public record NewsDto(long ThroughSnapshotId, IReadOnlyList<NewsItemDto> Items);

public record MarkNewsSeenRequest(long ThroughSnapshotId);

/// <summary>A list plus everything needed to render it in one request.</summary>
public record WatchListDetailDto(Guid Id, string Name, IReadOnlyList<TrackedItemDto> Items);

public record TrackedItemDto(
    Guid Id,
    string ProductCode,
    string Url,
    string? Name,
    /// <summary>Product image as an in-memory-cached data URI, so the browser never hits the CDN directly.</summary>
    string? ImageDataUri,
    decimal? CurrentPrice,
    /// <summary>Cheaper price for AlzaPlus+ members, when this product offers one.</summary>
    decimal? PlusPrice,
    /// <summary>Price with a discount code applied, when this product offers one.</summary>
    decimal? CouponPrice,
    string? Currency,
    string? Availability,
    DateTimeOffset? LastCheckedAt,
    string? LastError,
    bool IsActive,
    /// <summary>Position in the user's manual order. Zero-based; ties fall back to creation order.</summary>
    int SortOrder,
    DateTimeOffset CreatedAt,
    /// <summary>Full recorded history, oldest first — enough to draw the card's chart without a second call.</summary>
    IReadOnlyList<PriceSnapshotDto> History)
{
    // The range, the change and "cheapest yet" used to be derived here, from the
    // shelf price alone — which ignored the AlzaPlus+ and coupon prices and so
    // disagreed with the price the card actually showed. They are computed in the
    // client now, from this same history, because only the browser knows whether
    // the reader holds an AlzaPlus+ membership at the moment they are looking.
}
