namespace AlzaWatchdog.Api.Domain;

/// <summary>
/// The account. Its id is the key a person keeps to recover every list they own
/// on another device; it is not what appears in a bookmarkable list URL.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Whether this person actually holds an AlzaPlus+ membership. Off by default:
    /// a price most visitors cannot pay is worse than no price at all. The members'
    /// price is always scraped and stored regardless — this only decides whether it
    /// is shown and counted, so switching it on reveals the history already there.
    /// </summary>
    public bool HasAlzaPlus { get; set; }

    /// <summary>Where price notifications go. Null when this account wants none.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Set once the address has been confirmed by following the emailed link.
    /// Nothing but the confirmation itself is ever sent to an unconfirmed address:
    /// the person typing it here is not necessarily the person who owns it.
    /// </summary>
    public DateTimeOffset? EmailConfirmedAt { get; set; }

    /// <summary>The one-time secret in the confirmation link. Cleared once used.</summary>
    public string? EmailConfirmToken { get; set; }

    /// <summary>
    /// The newest snapshot this account has already been told about. The digest
    /// sends what is above this line and moves it up, so a failed send is retried
    /// on the next sweep rather than silently dropped.
    /// </summary>
    public long NotifiedThroughSnapshotId { get; set; }

    /// <summary>
    /// The newest snapshot this account has seen in the app's notification bell.
    /// Everything above it is shown as new; opening the bell moves it up. Kept on
    /// the server so every device holding the key agrees on what is new.
    /// </summary>
    public long SeenThroughSnapshotId { get; set; }

    public bool WantsNotifications => Email is not null && EmailConfirmedAt is not null;

    public List<WatchList> Lists { get; set; } = [];
}
