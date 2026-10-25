using System.Globalization;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

/// <summary>Pro: turn the monthly tune-up on or off. Turning it off is always allowed, Pro or not.</summary>
public sealed partial class TuneUpViewModel : ObservableObject
{
    public const string FeatureName = "The monthly tune-up";

    private static readonly CultureInfo UkEnglish = CultureInfo.GetCultureInfo("en-GB");

    private readonly ITuneUpScheduler _scheduler;
    private readonly ActionRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IAppLog _log;
    private readonly AppOptions _options;
    private readonly Func<bool> _isPro;

    private TuneUpStatus _status = new(false, null, null);
    private bool _isBusy;
    private string? _message;

    public TuneUpViewModel(ITuneUpScheduler scheduler, ActionRunner runner, IDialogService dialogs, IAppLog log, AppOptions options, Func<bool> isPro)
    {
        _scheduler = scheduler;
        _runner = runner;
        _dialogs = dialogs;
        _log = log;
        _options = options;
        _isPro = isPro;
    }

    public string WhatItDoes => TuneUp.WhatItDoes;

    public bool IsOn => _status.IsOn;

    public string StatusText => _status switch
    {
        { IsOn: false } => "The monthly tune-up is off.",
        { NextRun: { } next } => $"The monthly tune-up is on. Next one: {next.ToLocalTime().ToString("dddd d MMMM yyyy", UkEnglish)}.",
        _ => "The monthly tune-up is on.",
    };

    public string? LastRunText => _status.LastRun is { } last ? $"Last ran {last.ToLocalTime().ToString("d MMMM yyyy", UkEnglish)}." : null;

    public string ButtonText => IsOn ? "Turn off the monthly tune-up" : "Turn on the monthly tune-up";

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ToggleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            _status = await _scheduler.GetStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning("Couldn't check the monthly tune-up.", ex);
        }

        foreach (var name in new[] { nameof(IsOn), nameof(StatusText), nameof(LastRunText), nameof(ButtonText) })
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        var turnOn = !IsOn;
        if (turnOn && !_isPro())
        {
            _dialogs.ShowProRequired(FeatureName);
            return;
        }

        var plan = ActionPlan.Create(turnOn ? "Turn on the monthly tune-up" : "Turn off the monthly tune-up", [new ScheduleTuneUpAction(turnOn)]);
        var preview = plan.CreatePreview(_options.DryRun);
        if (!_dialogs.ConfirmPlan(preview))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var report = await Task.Run(() => _runner.RunAsync(preview.Confirm())).ConfigureAwait(true);
            Message = string.Join(" ", RunSummary.Describe(report));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Changing the monthly tune-up failed.", ex);
            Message = "Something went wrong. The details are in the log.";
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    private bool CanToggle() => !IsBusy;
}
