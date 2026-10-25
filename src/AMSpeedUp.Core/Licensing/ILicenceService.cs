namespace AMSpeedUp.Core.Licensing;

public enum LicenceOutcome
{
    /// <summary>The key is genuine, for this product, and active on this PC.</summary>
    Valid,

    /// <summary>The key doesn't exist, or this PC's activation was removed.</summary>
    NotFound,

    /// <summary>The key has expired, for example a subscription that ended.</summary>
    Expired,

    /// <summary>The seller switched the key off, for example after a refund.</summary>
    Disabled,

    /// <summary>The key is already in use on as many PCs as it allows.</summary>
    LimitReached,

    /// <summary>A genuine key, but for a different product or store.</summary>
    WrongProduct,

    /// <summary>Couldn't get an answer: no internet, the server was busy, and so on. Never a reason to remove Pro.</summary>
    NoAnswer,
}

/// <summary>What the licence server said, plus the details worth keeping.</summary>
public sealed record LicenceResponse(LicenceOutcome Outcome, string? ServerMessage = null, LicenceInfo? Info = null)
{
    public bool GotAnAnswer => Outcome != LicenceOutcome.NoAnswer;
}

public sealed record LicenceInfo
{
    public string? InstanceId { get; init; }

    public int? ActivationLimit { get; init; }

    public int? ActivationUsage { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? CustomerName { get; init; }
}

/// <summary>
/// Talks to whoever sells the licences. Lemon Squeezy today; another seller, such as Paddle, can be
/// added by writing another class with this interface, without touching the rest of the app.
/// </summary>
public interface ILicenceService
{
    /// <param name="instanceName">A label for this PC as the seller will show it. Not personal.</param>
    Task<LicenceResponse> ActivateAsync(string key, string instanceName, CancellationToken cancellationToken = default);

    Task<LicenceResponse> ValidateAsync(string key, string instanceId, CancellationToken cancellationToken = default);

    Task<LicenceResponse> DeactivateAsync(string key, string instanceId, CancellationToken cancellationToken = default);
}

/// <summary>The activation saved on this PC.</summary>
public sealed record StoredLicence
{
    public required string Key { get; init; }

    public required string InstanceId { get; init; }

    public required DateTimeOffset ActivatedAt { get; init; }

    /// <summary>The last time the seller confirmed the key is good.</summary>
    public required DateTimeOffset LastConfirmedAt { get; init; }

    /// <summary>The last time a check was tried, whether or not it got an answer.</summary>
    public DateTimeOffset? LastAttemptAt { get; init; }

    /// <summary>Set only when the seller clearly said the key is no longer good.</summary>
    public LicenceOutcome? RevokedBecause { get; init; }

    public int? ActivationLimit { get; init; }

    public int? ActivationUsage { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Keeps the activation on this PC, protected so it can't simply be copied to another PC.</summary>
public interface ILicenceStore
{
    StoredLicence? Load();

    void Save(StoredLicence licence);

    void Delete();
}
