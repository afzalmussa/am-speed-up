using System.Collections.ObjectModel;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

/// <summary>
/// The "Startup" tab. Switches only mark what the user wants; nothing changes until they click
/// Apply, see the preview and confirm it.
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupInventory _inventory;
    private readonly IReadOnlyList<KnownStartupItem> _known;
    private readonly ActionRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IAppLog _log;
    private readonly AppOptions _options;

    private bool _isBusy;
    private bool _hasLoaded;
    private string _busyText = "";
    private IReadOnlyList<string> _summary = [];

    public StartupViewModel(IStartupInventory inventory, IReadOnlyList<KnownStartupItem> known, ActionRunner runner, IDialogService dialogs, IAppLog log, AppOptions options)
    {
        _inventory = inventory;
        _known = known;
        _runner = runner;
        _dialogs = dialogs;
        _log = log;
        _options = options;
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowList));
                OnPropertyChanged(nameof(ShowEmpty));
                RefreshCommand.NotifyCanExecuteChanged();
                ApplyChangesCommand.NotifyCanExecuteChanged();
                ResetChangesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasLoaded
    {
        get => _hasLoaded;
        private set
        {
            if (SetProperty(ref _hasLoaded, value))
            {
                OnPropertyChanged(nameof(ShowList));
                OnPropertyChanged(nameof(ShowEmpty));
            }
        }
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public IReadOnlyList<string> Summary
    {
        get => _summary;
        private set
        {
            if (SetProperty(ref _summary, value))
            {
                OnPropertyChanged(nameof(HasSummary));
            }
        }
    }

    public bool HasSummary => Summary.Count > 0;

    public bool ShowList => HasLoaded && !IsBusy && Items.Count > 0;

    public bool ShowEmpty => HasLoaded && !IsBusy && Items.Count == 0;

    public int OnCount => Items.Count(i => i.Item.IsEnabled);

    public string CountText => $"{Items.Count} programs can start with Windows. {OnCount} of them are switched on.";

    public int PendingCount => Items.Count(i => i.IsChanged);

    public string ApplyButtonText => PendingCount switch
    {
        0 => "Apply changes",
        1 => "Apply 1 change",
        var n => $"Apply {n} changes",
    };

    /// <summary>Loads the list the first time the tab is opened.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!HasLoaded && !IsBusy)
        {
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => LoadAsync();

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyChangesAsync()
    {
        var changed = Items.Where(i => i.IsChanged).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var actions = changed.Select(i => new ChangeStartupAction(i.Item, i.IsOn, i.DisplayName)).ToList();
        var plan = ActionPlan.Create(actions.Count == 1 ? "Change 1 startup program" : $"Change {actions.Count} startup programs", actions);
        var preview = plan.CreatePreview(_options.DryRun);

        if (!_dialogs.ConfirmPlan(preview))
        {
            _log.Info("The user cancelled the startup changes at the preview. Nothing was changed.");
            return;
        }

        var confirmed = preview.Confirm();
        IsBusy = true;
        BusyText = "Saving your changes...";
        try
        {
            var report = await Task.Run(() => _runner.RunAsync(confirmed)).ConfigureAwait(true);
            Summary = [.. RunSummary.Describe(report), "Changes apply the next time you start your PC."];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Changing startup programs stopped with an error.", ex);
            Summary = ["Something went wrong part-way through. The details are in the log."];
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private bool CanApply() => !IsBusy && PendingCount > 0;

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void ResetChanges()
    {
        foreach (var item in Items)
        {
            item.IsOn = item.Item.IsEnabled;
        }
    }

    private bool CanReset() => !IsBusy && PendingCount > 0;

    private async Task LoadAsync()
    {
        IsBusy = true;
        BusyText = "Looking at what starts with Windows...";
        try
        {
            var result = await _inventory.ScanAsync().ConfigureAwait(true);
            Items.Clear();
            foreach (var item in result.Items
                         .Select(i => new StartupItemViewModel(i, StartupDescriber.Describe(i, _known), OnChanged))
                         .OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                Items.Add(item);
            }

            HasLoaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Listing startup programs failed.", ex);
            _dialogs.ShowMessage("Couldn't list startup programs", "Something went wrong while looking at what starts with Windows. Nothing was changed. The details are in the log.");
        }
        finally
        {
            IsBusy = false;
            OnChanged();
            OnPropertyChanged(nameof(CountText));
        }
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ApplyButtonText));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowEmpty));
        ApplyChangesCommand.NotifyCanExecuteChanged();
        ResetChangesCommand.NotifyCanExecuteChanged();
    }
}

public sealed class StartupItemViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private bool _isOn;

    public StartupItemViewModel(StartupItem item, StartupDescription description, Action onChanged)
    {
        Item = item;
        Description = description;
        _onChanged = onChanged;
        _isOn = item.IsEnabled;
    }

    public StartupItem Item { get; }

    public StartupDescription Description { get; }

    public string DisplayName => Description.DisplayName;

    public string Explanation => Description.Description;

    public string? AdviceText => Description.Advice switch
    {
        StartupAdvice.TurnOff => "Can be turned off",
        StartupAdvice.Optional => "Your choice",
        StartupAdvice.KeepOn => "Best left on",
        _ => null,
    };

    public bool HasAdvice => AdviceText is not null;

    /// <summary>For example "Spotify.exe, made by Spotify AB. Starts for everyone on this PC."</summary>
    public string DetailsText
    {
        get
        {
            var parts = new List<string>();
            if (Item.ProgramFileName is { } file)
            {
                parts.Add(Item.Publisher is { } publisher ? $"{file}, made by {publisher}." : $"{file}.");
            }

            parts.Add(Item.Source switch
            {
                StartupSource.RunAllUsers or StartupSource.RunAllUsers32 or StartupSource.StartupFolderAllUsers => "Starts for everyone on this PC.",
                StartupSource.ScheduledTask => "Starts as a scheduled task when you sign in.",
                _ => "Starts when you sign in.",
            });
            return string.Join(" ", parts);
        }
    }

    public bool IsProtected => Description.Protection.IsProtected;

    public string? ProtectionText => IsProtected ? Description.Protection.Reason : null;

    /// <summary>Protected items can't be switched off, but one that's already off can be switched back on.</summary>
    public bool CanChange => !IsProtected || !Item.IsEnabled;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (!CanChange && value != Item.IsEnabled)
            {
                return;
            }

            if (SetProperty(ref _isOn, value))
            {
                OnPropertyChanged(nameof(IsChanged));
                OnPropertyChanged(nameof(StateText));
                _onChanged();
            }
        }
    }

    public bool IsChanged => IsOn != Item.IsEnabled;

    public string StateText => (IsOn, IsChanged) switch
    {
        (true, false) => "On",
        (false, false) => "Off",
        (true, true) => "On (not applied yet)",
        _ => "Off (not applied yet)",
    };
}
