using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Core.Licensing;

public enum LicenceState
{
    /// <summary>No licence on this PC.</summary>
    Free,

    /// <summary>Confirmed recently.</summary>
    Pro,

    /// <summary>Couldn't be checked lately (no internet), but still within the grace period, so Pro keeps working.</summary>
    ProOffline,

    /// <summary>No successful check for longer than the grace period. Pro is paused until one succeeds.</summary>
    NeedsCheck,

    Expired,

    /// <summary>The seller switched the key off, or this PC's activation was removed.</summary>
    Revoked,

    /// <summary>A developer build pretending to be Pro, for testing. Never in release builds.</summary>
    DeveloperMode,
}

public sealed record LicenceStatus(LicenceState State, StoredLicence? Licence)
{
    public bool IsPro => State is LicenceState.Pro or LicenceState.ProOffline or LicenceState.DeveloperMode;
}

/// <param name="Succeeded">Whether it did what was asked.</param>
/// <param name="Message">A plain explanation for the user.</param>
public sealed record LicenceResult(bool Succeeded, string Message);

/// <summary>
/// Looks after the licence on this PC. The rules:
/// <list type="bullet">
/// <item>The key is activated once per PC. After that it's only checked, because Lemon Squeezy counts every activation as another PC.</item>
/// <item>It's checked online every 7 days.</item>
/// <item>If a check can't get an answer, Pro keeps working for 30 days from the last good check.</item>
/// <item>Only a clear answer from the seller (expired, switched off, not found) ends Pro straight away.</item>
/// </list>
/// </summary>
public sealed class LicenceManager
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromDays(7);
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(30);

    // Don't keep retrying a failed check every time the app opens.
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(6);

    private readonly ILicenceService _service;
    private readonly ILicenceStore _store;
    private readonly IAppLog _log;
    private readonly TimeProvider _time;
    private readonly bool _developerMode;
    private LicenceStatus _current;

    public LicenceManager(ILicenceService service, ILicenceStore store, IAppLog log, bool developerMode, TimeProvider? time = null)
    {
        _service = service;
        _store = store;
        _log = log;
        _developerMode = developerMode;
        _time = time ?? TimeProvider.System;
        _current = Evaluate(LoadSafely());

        if (developerMode)
        {
            _log.Warning("Developer mode is on: Pro is switched on without a licence. This only happens in developer builds.");
        }
    }

    public event Action? Changed;

    public LicenceStatus Current => _current;

    /// <summary>Works out the state from what's saved, using the rules above.</summary>
    public LicenceStatus Evaluate(StoredLicence? licence)
    {
        if (_developerMode)
        {
            return new LicenceStatus(LicenceState.DeveloperMode, licence);
        }

        if (licence is null)
        {
            return new LicenceStatus(LicenceState.Free, null);
        }

        if (licence.RevokedBecause is { } reason)
        {
            return new LicenceStatus(reason == LicenceOutcome.Expired ? LicenceState.Expired : LicenceState.Revoked, licence);
        }

        var sinceConfirmed = _time.GetUtcNow() - licence.LastConfirmedAt;
        if (sinceConfirmed <= CheckEvery)
        {
            return new LicenceStatus(LicenceState.Pro, licence);
        }

        return new LicenceStatus(sinceConfirmed <= OfflineGrace ? LicenceState.ProOffline : LicenceState.NeedsCheck, licence);
    }

    public async Task<LicenceResult> ActivateAsync(string enteredKey, CancellationToken cancellationToken = default)
    {
        var key = enteredKey.Trim();
        if (key.Length < 8)
        {
            return new LicenceResult(false, LicenceText.KeyLooksWrong);
        }

        // Already activated with this key: just check it, so another PC slot isn't used up.
        if (LoadSafely() is { } existing && string.Equals(existing.Key, key, StringComparison.OrdinalIgnoreCase) && existing.RevokedBecause is null)
        {
            _log.Info($"Licence {LicenceText.Mask(key)} is already active on this PC. Checking it instead of activating again.");
            await CheckNowAsync(cancellationToken).ConfigureAwait(false);
            return new LicenceResult(Current.IsPro, Current.IsPro ? LicenceText.AlreadyActive : LicenceText.Describe(Current));
        }

        _log.Info($"Activating licence {LicenceText.Mask(key)}.");
        var response = await _service.ActivateAsync(key, NewInstanceName(), cancellationToken).ConfigureAwait(false);
        _log.Info($"Activation answer: {response.Outcome}. {response.ServerMessage}".TrimEnd());

        if (response.Outcome != LicenceOutcome.Valid || response.Info?.InstanceId is not { } instanceId)
        {
            return new LicenceResult(false, LicenceText.ActivationProblem(response.Outcome));
        }

        var now = _time.GetUtcNow();
        Save(new StoredLicence
        {
            Key = key,
            InstanceId = instanceId,
            ActivatedAt = now,
            LastConfirmedAt = now,
            LastAttemptAt = now,
            ActivationLimit = response.Info.ActivationLimit,
            ActivationUsage = response.Info.ActivationUsage,
            ExpiresAt = response.Info.ExpiresAt,
        });
        return new LicenceResult(true, LicenceText.Activated);
    }

    /// <summary>Checks with the seller if a check is due. Called when the app opens.</summary>
    public async Task CheckIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (LoadSafely() is not { RevokedBecause: null } licence)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var due = now - licence.LastConfirmedAt > CheckEvery
                  && (licence.LastAttemptAt is not { } attempt || now - attempt > RetryAfterFailure);
        if (due)
        {
            await CheckNowAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Checks with the seller now, for the "Check now" button.</summary>
    public async Task<LicenceResult> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (LoadSafely() is not { } licence)
        {
            return new LicenceResult(false, LicenceText.Describe(Current));
        }

        _log.Info($"Checking licence {LicenceText.Mask(licence.Key)}.");
        var response = await _service.ValidateAsync(licence.Key, licence.InstanceId, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        _log.Info($"Licence check answer: {response.Outcome}. {response.ServerMessage}".TrimEnd());

        var updated = response.Outcome switch
        {
            LicenceOutcome.Valid => licence with
            {
                LastConfirmedAt = now,
                LastAttemptAt = now,
                RevokedBecause = null,
                ActivationLimit = response.Info?.ActivationLimit ?? licence.ActivationLimit,
                ActivationUsage = response.Info?.ActivationUsage ?? licence.ActivationUsage,
                ExpiresAt = response.Info?.ExpiresAt ?? licence.ExpiresAt,
            },

            // No answer is never held against the customer: only the attempt time changes.
            LicenceOutcome.NoAnswer => licence with { LastAttemptAt = now },

            _ => licence with { LastAttemptAt = now, RevokedBecause = response.Outcome },
        };

        Save(updated);
        if (response.Outcome != LicenceOutcome.Valid)
        {
            _log.Warning($"Licence is now: {Current.State}.");
        }

        return new LicenceResult(response.Outcome == LicenceOutcome.Valid, LicenceText.Describe(Current));
    }

    /// <summary>"Remove this PC": gives the activation back so the licence can be used on another PC.</summary>
    public async Task<LicenceResult> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        if (LoadSafely() is not { } licence)
        {
            return new LicenceResult(true, LicenceText.Describe(Current));
        }

        _log.Info($"Removing licence {LicenceText.Mask(licence.Key)} from this PC.");
        var response = await _service.DeactivateAsync(licence.Key, licence.InstanceId, cancellationToken).ConfigureAwait(false);
        _log.Info($"Removal answer: {response.Outcome}. {response.ServerMessage}".TrimEnd());

        if (response.Outcome == LicenceOutcome.NoAnswer)
        {
            // Keep it: if it were deleted here, the PC slot would stay used up with no way to free it.
            return new LicenceResult(false, LicenceText.RemoveNeedsInternet);
        }

        // Valid means removed. NotFound means it was already gone. Either way this PC is done with it.
        DeleteSafely();
        return new LicenceResult(true, LicenceText.Removed);
    }

    /// <summary>A label for this PC that says nothing about the person or the computer.</summary>
    public static string NewInstanceName() => "AM Speed Up PC " + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private StoredLicence? LoadSafely()
    {
        try
        {
            return _store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            _log.Warning("The saved licence couldn't be read. Enter the licence key again to fix this.", ex);
            return null;
        }
    }

    private void Save(StoredLicence licence)
    {
        _store.Save(licence);
        Update(Evaluate(licence));
    }

    private void DeleteSafely()
    {
        _store.Delete();
        Update(Evaluate(null));
    }

    private void Update(LicenceStatus status)
    {
        _current = status;
        Changed?.Invoke();
    }
}
