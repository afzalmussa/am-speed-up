using AMSpeedUp.Core.Licensing;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace AMSpeedUp.Tests.Licensing;

internal sealed class FakeLicenceService : ILicenceService
{
    public LicenceResponse ActivateAnswer { get; set; } = new(LicenceOutcome.Valid, null, new LicenceInfo { InstanceId = "instance-1", ActivationLimit = 3, ActivationUsage = 1 });

    public LicenceResponse ValidateAnswer { get; set; } = new(LicenceOutcome.Valid, null, new LicenceInfo { ActivationLimit = 3, ActivationUsage = 2 });

    public LicenceResponse DeactivateAnswer { get; set; } = new(LicenceOutcome.Valid);

    public List<string> Calls { get; } = [];

    public Task<LicenceResponse> ActivateAsync(string key, string instanceName, CancellationToken cancellationToken = default)
    {
        Calls.Add($"activate {key} {instanceName}");
        return Task.FromResult(ActivateAnswer);
    }

    public Task<LicenceResponse> ValidateAsync(string key, string instanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"validate {key} {instanceId}");
        return Task.FromResult(ValidateAnswer);
    }

    public Task<LicenceResponse> DeactivateAsync(string key, string instanceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"deactivate {key} {instanceId}");
        return Task.FromResult(DeactivateAnswer);
    }
}

internal sealed class MemoryLicenceStore : ILicenceStore
{
    public StoredLicence? Saved { get; set; }

    public Exception? ThrowOnLoad { get; set; }

    public StoredLicence? Load() => ThrowOnLoad is not null ? throw ThrowOnLoad : Saved;

    public void Save(StoredLicence licence) => Saved = licence;

    public void Delete() => Saved = null;
}

public class LicenceManagerTests
{
    private const string GoodKey = "38B1460A-5104-4067-A91D-77B872934D51";

    private readonly FakeLicenceService _service = new();
    private readonly MemoryLicenceStore _store = new();
    private readonly MemoryLog _log = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private LicenceManager Create(bool developerMode = false) => new(_service, _store, _log, developerMode, _time);

    private StoredLicence Stored(TimeSpan confirmedAgo, TimeSpan? attemptedAgo = null, LicenceOutcome? revoked = null) => new()
    {
        Key = GoodKey,
        InstanceId = "instance-1",
        ActivatedAt = _time.GetUtcNow().AddDays(-60),
        LastConfirmedAt = _time.GetUtcNow() - confirmedAgo,
        LastAttemptAt = attemptedAgo is { } a ? _time.GetUtcNow() - a : null,
        RevokedBecause = revoked,
        ActivationLimit = 3,
        ActivationUsage = 1,
    };

    // Valid

    [Fact]
    public void With_no_licence_it_is_the_free_version()
    {
        var manager = Create();

        Assert.Equal(LicenceState.Free, manager.Current.State);
        Assert.False(manager.Current.IsPro);
        Assert.Equal("Free version", LicenceText.Badge(manager.Current));
    }

    [Fact]
    public async Task Activating_a_good_key_switches_on_Pro_and_saves_it()
    {
        var manager = Create();
        var changed = 0;
        manager.Changed += () => changed++;

        var result = await manager.ActivateAsync("  " + GoodKey + "  ");

        Assert.True(result.Succeeded);
        Assert.Equal(LicenceText.Activated, result.Message);
        Assert.Equal(LicenceState.Pro, manager.Current.State);
        Assert.True(manager.Current.IsPro);
        Assert.Equal(GoodKey, _store.Saved!.Key);
        Assert.Equal("instance-1", _store.Saved.InstanceId);
        Assert.Equal(_time.GetUtcNow(), _store.Saved.LastConfirmedAt);
        Assert.Equal(1, changed);
        Assert.StartsWith($"activate {GoodKey} AM Speed Up PC ", Assert.Single(_service.Calls));
    }

    [Fact]
    public async Task The_same_key_on_the_same_PC_is_checked_not_activated_again()
    {
        // Lemon Squeezy counts every activation as another PC, even from the same one.
        _store.Saved = Stored(TimeSpan.FromDays(2));
        var manager = Create();

        var result = await manager.ActivateAsync(GoodKey.ToLowerInvariant());

        Assert.True(result.Succeeded);
        Assert.Equal(LicenceText.AlreadyActive, result.Message);
        Assert.Equal(["validate 38B1460A-5104-4067-A91D-77B872934D51 instance-1"], _service.Calls);
    }

    [Fact]
    public void PC_labels_are_random_and_say_nothing_about_the_person()
    {
        var first = LicenceManager.NewInstanceName();
        var second = LicenceManager.NewInstanceName();

        Assert.Matches("^AM Speed Up PC [0-9A-F]{8}$", first);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(Environment.MachineName, first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, first, StringComparison.OrdinalIgnoreCase);
    }

    // Invalid

    [Theory]
    [InlineData(LicenceOutcome.NotFound, "wasn't recognised")]
    [InlineData(LicenceOutcome.Expired, "expired")]
    [InlineData(LicenceOutcome.Disabled, "switched off")]
    [InlineData(LicenceOutcome.LimitReached, "Remove this PC")]
    [InlineData(LicenceOutcome.WrongProduct, "different product")]
    [InlineData(LicenceOutcome.NoAnswer, "internet connection")]
    public async Task A_rejected_key_explains_why_and_saves_nothing(LicenceOutcome outcome, string mentions)
    {
        _service.ActivateAnswer = new LicenceResponse(outcome);
        var manager = Create();

        var result = await manager.ActivateAsync(GoodKey);

        Assert.False(result.Succeeded);
        Assert.Contains(mentions, result.Message);
        Assert.Null(_store.Saved);
        Assert.Equal(LicenceState.Free, manager.Current.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public async Task Obviously_wrong_keys_are_caught_before_going_online(string key)
    {
        var result = await Create().ActivateAsync(key);

        Assert.False(result.Succeeded);
        Assert.Equal(LicenceText.KeyLooksWrong, result.Message);
        Assert.Empty(_service.Calls);
    }

    // Revalidation and offline grace

    [Fact]
    public async Task No_check_is_made_within_seven_days_of_the_last_one()
    {
        _store.Saved = Stored(TimeSpan.FromDays(6));
        var manager = Create();

        await manager.CheckIfDueAsync();

        Assert.Empty(_service.Calls);
        Assert.Equal(LicenceState.Pro, manager.Current.State);
    }

    [Fact]
    public async Task After_seven_days_a_good_check_renews_it()
    {
        _store.Saved = Stored(TimeSpan.FromDays(8));
        var manager = Create();

        await manager.CheckIfDueAsync();

        Assert.Single(_service.Calls);
        Assert.Equal(_time.GetUtcNow(), _store.Saved!.LastConfirmedAt);
        Assert.Equal(2, _store.Saved.ActivationUsage);
        Assert.Equal(LicenceState.Pro, manager.Current.State);
    }

    [Fact]
    public async Task No_internet_within_30_days_keeps_Pro_working()
    {
        _store.Saved = Stored(TimeSpan.FromDays(20));
        _service.ValidateAnswer = new LicenceResponse(LicenceOutcome.NoAnswer, "No such host is known.");
        var manager = Create();

        await manager.CheckIfDueAsync();

        Assert.Equal(LicenceState.ProOffline, manager.Current.State);
        Assert.True(manager.Current.IsPro);
        Assert.Equal(_time.GetUtcNow(), _store.Saved!.LastAttemptAt);
        Assert.Equal(_time.GetUtcNow().AddDays(-20), _store.Saved.LastConfirmedAt);
        Assert.Null(_store.Saved.RevokedBecause);
    }

    [Fact]
    public void After_30_days_without_a_good_check_Pro_is_paused_not_removed()
    {
        _store.Saved = Stored(TimeSpan.FromDays(31));
        var manager = Create();

        Assert.Equal(LicenceState.NeedsCheck, manager.Current.State);
        Assert.False(manager.Current.IsPro);
        Assert.NotNull(_store.Saved);
        Assert.Contains("Check now", LicenceText.Describe(manager.Current));
    }

    [Fact]
    public async Task A_good_check_after_the_grace_period_brings_Pro_straight_back()
    {
        _store.Saved = Stored(TimeSpan.FromDays(45));
        var manager = Create();

        var result = await manager.CheckNowAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(LicenceState.Pro, manager.Current.State);
    }

    [Fact]
    public async Task A_failed_check_is_not_retried_every_time_the_app_opens()
    {
        _store.Saved = Stored(TimeSpan.FromDays(10), attemptedAgo: TimeSpan.FromHours(1));
        var manager = Create();

        await manager.CheckIfDueAsync();

        Assert.Empty(_service.Calls);
    }

    [Fact]
    public void Exactly_seven_and_exactly_30_days_are_still_inside_the_limits()
    {
        _store.Saved = Stored(TimeSpan.FromDays(7));
        Assert.Equal(LicenceState.Pro, Create().Current.State);

        _store.Saved = Stored(TimeSpan.FromDays(30));
        Assert.Equal(LicenceState.ProOffline, Create().Current.State);
    }

    // Expired and switched off

    [Fact]
    public async Task An_expired_answer_ends_Pro_straight_away_and_says_so()
    {
        _store.Saved = Stored(TimeSpan.FromDays(8));
        _service.ValidateAnswer = new LicenceResponse(LicenceOutcome.Expired);
        var manager = Create();

        await manager.CheckIfDueAsync();

        Assert.Equal(LicenceState.Expired, manager.Current.State);
        Assert.False(manager.Current.IsPro);
        Assert.Equal("Pro expired", LicenceText.Badge(manager.Current));
        Assert.Contains("expired", LicenceText.Describe(manager.Current));
    }

    [Theory]
    [InlineData(LicenceOutcome.Disabled)]
    [InlineData(LicenceOutcome.NotFound)]
    [InlineData(LicenceOutcome.WrongProduct)]
    public async Task A_switched_off_or_removed_key_ends_Pro_and_says_so(LicenceOutcome outcome)
    {
        _store.Saved = Stored(TimeSpan.FromDays(8));
        _service.ValidateAnswer = new LicenceResponse(outcome);
        var manager = Create();

        await manager.CheckIfDueAsync();

        Assert.Equal(LicenceState.Revoked, manager.Current.State);
        Assert.False(manager.Current.IsPro);
        Assert.Equal(outcome, _store.Saved!.RevokedBecause);
    }

    [Fact]
    public async Task A_key_that_was_switched_off_and_on_again_comes_back_after_a_good_check()
    {
        _store.Saved = Stored(TimeSpan.FromDays(8), revoked: LicenceOutcome.Disabled);
        var manager = Create();

        await manager.CheckNowAsync();

        Assert.Equal(LicenceState.Pro, manager.Current.State);
        Assert.Null(_store.Saved!.RevokedBecause);
    }

    // Removing this PC

    [Fact]
    public async Task Removing_this_PC_gives_the_slot_back_and_returns_to_free()
    {
        _store.Saved = Stored(TimeSpan.FromDays(1));
        var manager = Create();

        var result = await manager.DeactivateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(["deactivate 38B1460A-5104-4067-A91D-77B872934D51 instance-1"], _service.Calls);
        Assert.Null(_store.Saved);
        Assert.Equal(LicenceState.Free, manager.Current.State);
    }

    [Fact]
    public async Task Removing_this_PC_needs_the_internet_and_keeps_Pro_if_offline()
    {
        _store.Saved = Stored(TimeSpan.FromDays(1));
        _service.DeactivateAnswer = new LicenceResponse(LicenceOutcome.NoAnswer);
        var manager = Create();

        var result = await manager.DeactivateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(LicenceText.RemoveNeedsInternet, result.Message);
        Assert.NotNull(_store.Saved);
        Assert.True(manager.Current.IsPro);
    }

    [Fact]
    public async Task Removing_a_PC_the_seller_already_forgot_still_clears_it_here()
    {
        _store.Saved = Stored(TimeSpan.FromDays(1));
        _service.DeactivateAnswer = new LicenceResponse(LicenceOutcome.NotFound);
        var manager = Create();

        Assert.True((await manager.DeactivateAsync()).Succeeded);
        Assert.Null(_store.Saved);
    }

    // Developer mode

    [Fact]
    public void Developer_mode_turns_on_Pro_without_a_licence_and_says_so()
    {
        var manager = Create(developerMode: true);

        Assert.Equal(LicenceState.DeveloperMode, manager.Current.State);
        Assert.True(manager.Current.IsPro);
        Assert.Equal("Pro (developer mode)", LicenceText.Badge(manager.Current));
        Assert.True(_log.Has(LogLevel.Warning, "Developer mode is on"));
    }

    [Fact]
    public void Developer_mode_needs_the_environment_variable()
    {
        Assert.False(DeveloperMode.IsOn(_ => null));
        Assert.False(DeveloperMode.IsOn(_ => "0"));
        Assert.Equal(DeveloperMode.IsCompiledIn, DeveloperMode.IsOn(name => name == "AMSPEEDUP_DEV_PRO" ? "1" : null));
    }

    [Fact]
    public void Developer_mode_only_exists_in_debug_builds()
    {
#if DEBUG
        Assert.True(DeveloperMode.IsCompiledIn);
#else
        Assert.False(DeveloperMode.IsCompiledIn);
        Assert.False(DeveloperMode.IsOn(_ => "1"));
#endif
    }

    // Privacy and robustness

    [Fact]
    public async Task The_full_key_is_never_written_to_the_log()
    {
        var manager = Create();
        await manager.ActivateAsync(GoodKey);
        await manager.CheckNowAsync();
        await manager.DeactivateAsync();

        Assert.NotEmpty(_log.Entries);
        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains(GoodKey, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_log.Entries, e => e.Message.Contains("38B1...4D51"));
    }

    [Theory]
    [InlineData("38B1460A-5104-4067-A91D-77B872934D51", "38B1...4D51")]
    [InlineData("  ABCDEFGHIJ  ", "ABCD...GHIJ")]
    [InlineData("SHORT", "*****")]
    public void Keys_are_masked_for_the_log(string key, string expected)
    {
        Assert.Equal(expected, LicenceText.Mask(key));
    }

    [Fact]
    public void A_damaged_saved_licence_falls_back_to_free_with_a_warning()
    {
        _store.ThrowOnLoad = new System.Security.Cryptography.CryptographicException("bad data");

        var manager = Create();

        Assert.Equal(LicenceState.Free, manager.Current.State);
        Assert.True(_log.Has(LogLevel.Warning, "couldn't be read"));
    }

    [Fact]
    public void Activations_are_shown_plainly()
    {
        Assert.Equal("Used on 2 of 3 PCs.", LicenceText.Activations(Stored(TimeSpan.Zero) with { ActivationUsage = 2 }));
        Assert.Equal("Used on 1 of 1 PC.", LicenceText.Activations(Stored(TimeSpan.Zero) with { ActivationLimit = 1 }));
        Assert.Null(LicenceText.Activations(Stored(TimeSpan.Zero) with { ActivationLimit = null }));
        Assert.Null(LicenceText.Activations(null));
    }

    [Fact]
    public void Every_state_has_a_badge_and_a_description()
    {
        foreach (var state in Enum.GetValues<LicenceState>())
        {
            var status = new LicenceStatus(state, null);
            Assert.False(string.IsNullOrWhiteSpace(LicenceText.Badge(status)));
            Assert.False(string.IsNullOrWhiteSpace(LicenceText.Describe(status)));
        }
    }
}
