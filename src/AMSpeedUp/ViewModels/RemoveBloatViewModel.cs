using System.Collections.ObjectModel;
using System.Globalization;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.Core.VendorTools;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

/// <summary>The "Remove bloat" tab: scan, choose, preview, remove, and see what was removed before.</summary>
public sealed partial class RemoveBloatViewModel : ObservableObject
{
    private readonly IAppInventory _inventory;
    private readonly IReadOnlyList<Preset> _presets;
    private readonly ActionRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shell;
    private readonly IUndoJournal _undo;
    private readonly IAntivirusCheckAfterRestart _afterRestart;
    private readonly IAppLog _log;
    private readonly AppOptions _options;
    private readonly Func<bool> _isPro;

    private bool _isBusy;
    private string _busyText = "";
    private bool _hasScanned;
    private IReadOnlyList<string> _summary = [];

    public RemoveBloatViewModel(
        IAppInventory inventory,
        IReadOnlyList<Preset> presets,
        ActionRunner runner,
        IDialogService dialogs,
        IShellLauncher shell,
        IUndoJournal undo,
        IAntivirusCheckAfterRestart afterRestart,
        IAppLog log,
        AppOptions options,
        Func<bool> isPro)
    {
        _inventory = inventory;
        _presets = presets;
        _runner = runner;
        _dialogs = dialogs;
        _shell = shell;
        _undo = undo;
        _afterRestart = afterRestart;
        _log = log;
        _options = options;
        _isPro = isPro;
        LoadHistory();
    }

    public ObservableCollection<PresetGroupViewModel> Groups { get; } = [];

    public ObservableCollection<RemovedAppViewModel> History { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowStart));
                OnPropertyChanged(nameof(ShowNothingFound));
                OnPropertyChanged(nameof(ShowResults));
                ScanCommand.NotifyCanExecuteChanged();
                RemoveSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public bool HasScanned
    {
        get => _hasScanned;
        private set
        {
            if (SetProperty(ref _hasScanned, value))
            {
                OnPropertyChanged(nameof(ShowStart));
                OnPropertyChanged(nameof(ShowNothingFound));
                OnPropertyChanged(nameof(ShowResults));
                OnPropertyChanged(nameof(ScanButtonText));
            }
        }
    }

    /// <summary>What happened last time something was removed, a line per point.</summary>
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

    public bool ShowStart => !HasScanned && !IsBusy;

    public bool ShowNothingFound => HasScanned && !IsBusy && Groups.Count == 0;

    public bool ShowResults => HasScanned && !IsBusy && Groups.Count > 0;

    public bool HasHistory => History.Count > 0;

    public string ScanButtonText => HasScanned ? "Scan again" : "Scan my PC";

    public int SelectedCount => Groups.Sum(g => g.Items.Count(i => i.IsSelected));

    public string RemoveButtonText => SelectedCount switch
    {
        0 => "Remove selected",
        1 => "Remove 1 app",
        var n => $"Remove {n} apps",
    };

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        BusyText = "Looking at what's installed. This can take a minute...";
        try
        {
            var inventory = await _inventory.ScanAsync().ConfigureAwait(true);
            var matches = PresetMatcher.Match(_presets, inventory.Apps, _isPro());

            foreach (var skipped in matches.ProtectedSkipped)
            {
                _log.Info($"Protected, so not offered: {skipped.App.DisplayName}. {skipped.Reason}");
            }

            Groups.Clear();
            foreach (var group in matches.Groups)
            {
                Groups.Add(new PresetGroupViewModel(group, OnSelectionChanged));
            }

            _log.Info($"Scan found {Groups.Sum(g => g.Items.Count)} app(s) the presets suggest looking at: {string.Join(", ", Groups.SelectMany(g => g.Items).Select(i => i.Name))}.");
            HasScanned = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("The scan failed.", ex);
            _dialogs.ShowMessage("The scan didn't finish", "Something went wrong while looking at what's installed. Nothing was changed. The details are in the log.");
        }
        finally
        {
            IsBusy = false;
            OnSelectionChanged();
        }
    }

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveSelectedAsync()
    {
        var chosen = Groups.SelectMany(g => g.Items).Where(i => i.IsSelected && !i.IsLocked).ToList();
        if (chosen.Count == 0)
        {
            return;
        }

        var actions = RemoveAppAction.InSafeOrder(chosen.Select(i => new RemoveAppAction(i.Match.App with { DisplayName = i.Name }, i.Match.Entry)));
        var plan = ActionPlan.Create(actions.Count == 1 ? "Remove 1 app" : $"Remove {actions.Count} apps", actions);
        var preview = plan.CreatePreview(_options.DryRun);

        _log.Info($"Showing the preview for {plan.Actions.Count} removal(s).");
        if (!_dialogs.ConfirmPlan(preview))
        {
            _log.Info("The user cancelled at the preview. Nothing was changed.");
            return;
        }

        var confirmed = preview.Confirm();
        IsBusy = true;
        BusyText = plan.NeedsRestorePoint && !_options.DryRun ? "Saving a restore point first. This can take a few minutes..." : "Starting...";
        var progress = new Progress<RunProgress>(p => BusyText = p.Completed < p.Total
            ? $"Working on {p.Completed + 1} of {p.Total}: {p.Current}"
            : "Finishing off...");

        try
        {
            var report = await Task.Run(() => _runner.RunAsync(confirmed, progress)).ConfigureAwait(true);
            Summary = RunSummary.Describe(report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Removing apps stopped with an error.", ex);
            Summary = ["Something went wrong part-way through. The details are in the log."];
        }
        finally
        {
            // Still running, so the PC didn't restart: the check after restart isn't needed.
            _afterRestart.Cancel();
            IsBusy = false;
        }

        LoadHistory();
        await ScanAsync().ConfigureAwait(true);
    }

    private bool CanRemove() => !IsBusy && SelectedCount > 0;

    [RelayCommand]
    private void Reinstall(RemovedAppViewModel item)
    {
        _log.Info($"Opening the Microsoft Store to reinstall {item.Name}.");
        if (!_shell.OpenStoreSearch(item.Name))
        {
            _dialogs.ShowMessage("Couldn't open the Microsoft Store", $"Open the Microsoft Store yourself and search for \"{item.Name}\".");
        }
    }

    [RelayCommand]
    private void OpenSystemRestore()
    {
        _log.Info("Opening System Restore.");
        _shell.OpenWindowsPage(WindowsPage.SystemRestore);
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(RemoveButtonText));
        OnPropertyChanged(nameof(ShowNothingFound));
        OnPropertyChanged(nameof(ShowResults));
        RemoveSelectedCommand.NotifyCanExecuteChanged();
    }

    private void LoadHistory()
    {
        History.Clear();
        try
        {
            foreach (var entry in _undo.GetAll()
                         .Where(e => e.Kind is UndoKind.StoreAppRemoved or UndoKind.DesktopProgramRemoved)
                         .OrderByDescending(e => e.When)
                         .Take(30))
            {
                History.Add(new RemovedAppViewModel(entry));
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _log.Warning("Couldn't read the list of removed apps.", ex);
        }

        OnPropertyChanged(nameof(HasHistory));
    }
}

public sealed class PresetGroupViewModel
{
    public PresetGroupViewModel(PresetGroup group, Action onSelectionChanged)
    {
        Name = group.Preset.Name;
        Description = group.Preset.Description;
        IsLocked = group.IsLocked;
        Items = group.Items.Select(i => new AppItemViewModel(i, onSelectionChanged)).ToList();
    }

    public string Name { get; }

    public string Description { get; }

    public bool IsLocked { get; }

    public IReadOnlyList<AppItemViewModel> Items { get; }
}

public sealed class AppItemViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;
    private bool _isSelected;

    public AppItemViewModel(MatchedApp match, Action onSelectionChanged)
    {
        Match = match;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = match.IsTickedByDefault;
    }

    public MatchedApp Match { get; }

    public string Name => Match.DisplayName;

    public string Description => Match.Entry.Description;

    public bool IsLocked => Match.IsLocked;

    public bool CanChange => !IsLocked;

    public string RecommendationText => IsLocked
        ? "Needs AM Speed Up Pro"
        : Match.Recommendation switch
        {
            Recommendation.Remove => "Recommended to remove",
            Recommendation.Optional => "Your choice",
            _ => "Worth keeping",
        };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (IsLocked)
            {
                value = false;
            }

            if (SetProperty(ref _isSelected, value))
            {
                _onSelectionChanged();
            }
        }
    }
}

public sealed class RemovedAppViewModel(UndoEntry entry)
{
    private static readonly CultureInfo UkEnglish = CultureInfo.GetCultureInfo("en-GB");

    public UndoEntry Entry { get; } = entry;

    public string Name => Entry.DisplayName;

    public string WhenText => "Removed " + Entry.When.ToLocalTime().ToString("d MMMM yyyy", UkEnglish);

    public bool CanReinstall => Entry.Kind == UndoKind.StoreAppRemoved;

    public string UndoText => CanReinstall
        ? "You can reinstall it free from the Microsoft Store."
        : "AM Speed Up can't put this back. Use System Restore to wind your PC back to before it was removed.";
}
