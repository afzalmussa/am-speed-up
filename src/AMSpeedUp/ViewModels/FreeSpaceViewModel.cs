using System.Collections.ObjectModel;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Reports;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

/// <summary>The "Free up space" tab: measure first, then delete only what the user ticks and confirms.</summary>
public sealed partial class FreeSpaceViewModel : ObservableObject
{
    private readonly ICleanupScanner _scanner;
    private readonly ActionRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shell;
    private readonly IAppLog _log;
    private readonly AppOptions _options;

    private bool _isBusy;
    private bool _hasScanned;
    private string _busyText = "";
    private string? _driveText;
    private IReadOnlyList<string> _summary = [];

    public FreeSpaceViewModel(
        ICleanupScanner scanner,
        ActionRunner runner,
        ActivityStatus status,
        IDialogService dialogs,
        IShellLauncher shell,
        IAppLog log,
        AppOptions options,
        LargeFilesViewModel? largeFiles = null,
        TuneUpViewModel? tuneUp = null)
    {
        LargeFiles = largeFiles;
        TuneUp = tuneUp;
        _scanner = scanner;
        _runner = runner;
        _dialogs = dialogs;
        _shell = shell;
        _log = log;
        _options = options;

        // Live progress from long jobs. WPF passes simple property changes to the screen from any thread.
        status.Changed += message =>
        {
            if (IsBusy)
            {
                BusyText = message;
            }
        };
    }

    public ObservableCollection<CleanupItemViewModel> Items { get; } = [];

    /// <summary>Notes the free space each time it's measured, for the before and after report.</summary>
    public SessionRecord? Session { get; init; }

    /// <summary>Pro: the large file and duplicate finder shown under the clean-up list.</summary>
    public LargeFilesViewModel? LargeFiles { get; }

    public bool HasLargeFileFinder => LargeFiles is not null;

    /// <summary>Pro: the monthly tune-up switch.</summary>
    public TuneUpViewModel? TuneUp { get; }

    public bool HasTuneUp => TuneUp is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowStart));
                OnPropertyChanged(nameof(ShowResults));
                ScanCommand.NotifyCanExecuteChanged();
                FreeUpSpaceCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasScanned
    {
        get => _hasScanned;
        private set
        {
            if (SetProperty(ref _hasScanned, value))
            {
                OnPropertyChanged(nameof(ShowStart));
                OnPropertyChanged(nameof(ShowResults));
                OnPropertyChanged(nameof(ScanButtonText));
            }
        }
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    /// <summary>For example "Drive C: has 23 GB free out of 237 GB."</summary>
    public string? DriveText
    {
        get => _driveText;
        private set => SetProperty(ref _driveText, value);
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

    public bool ShowStart => !HasScanned && !IsBusy;

    public bool ShowResults => HasScanned && !IsBusy;

    public string ScanButtonText => HasScanned ? "Check again" : "Check how much space I can free";

    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.Target.Bytes);

    public int SelectedCount => Items.Count(i => i.IsSelected);

    public string FreeButtonText => SelectedCount == 0
        ? "Free up space"
        : SelectedBytes > 0 ? $"Free up {ByteSize.Format(SelectedBytes)}" : "Free up space";

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        BusyText = "Measuring how much space can be freed. This can take a minute...";
        try
        {
            var result = await _scanner.ScanAsync().ConfigureAwait(true);
            Items.Clear();
            foreach (var target in result.Targets.OrderBy(t => t.Category))
            {
                Items.Add(new CleanupItemViewModel(target, OnSelectionChanged));
            }

            DriveText = Describe(result.SystemDrive);
            Session?.RecordFreeSpace(result.SystemDrive);
            HasScanned = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Measuring free space failed.", ex);
            _dialogs.ShowMessage("Couldn't check the space", "Something went wrong while measuring. Nothing was deleted. The details are in the log.");
        }
        finally
        {
            IsBusy = false;
            OnSelectionChanged();
        }
    }

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanFree))]
    private async Task FreeUpSpaceAsync()
    {
        var actions = Items.Where(i => i.IsSelected).Select(i => new CleanupAction(i.Target)).ToList();
        if (actions.Count == 0)
        {
            return;
        }

        var plan = ActionPlan.Create("Free up space", actions);
        var preview = plan.CreatePreview(_options.DryRun);
        if (!_dialogs.ConfirmPlan(preview))
        {
            _log.Info("The user cancelled the clean-up at the preview. Nothing was deleted.");
            return;
        }

        var confirmed = preview.Confirm();
        var before = _scanner.GetSystemDriveSpace();
        IsBusy = true;
        BusyText = "Starting...";
        try
        {
            var report = await Task.Run(() => _runner.RunAsync(confirmed)).ConfigureAwait(true);
            var lines = RunSummary.Describe(report).ToList();
            if (!_options.DryRun && before is not null && _scanner.GetSystemDriveSpace() is { } after)
            {
                var gained = after.FreeBytes - before.FreeBytes;
                lines.Add(gained > 0
                    ? $"Drive {after.Drive} now has {ByteSize.Format(after.FreeBytes)} free, {ByteSize.Format(gained)} more than before."
                    : $"Drive {after.Drive} now has {ByteSize.Format(after.FreeBytes)} free.");
            }

            Summary = lines;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Freeing up space stopped with an error.", ex);
            Summary = ["Something went wrong part-way through. The details are in the log."];
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync().ConfigureAwait(true);
    }

    private bool CanFree() => !IsBusy && SelectedCount > 0;

    [RelayCommand]
    private void OpenRecycleBin() => _shell.OpenWindowsPage(WindowsPage.RecycleBin);

    private static string? Describe(DriveSpace? drive) =>
        drive is null ? null : $"Drive {drive.Drive} has {ByteSize.Format(drive.FreeBytes)} free out of {ByteSize.Format(drive.TotalBytes)}.";

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(FreeButtonText));
        FreeUpSpaceCommand.NotifyCanExecuteChanged();
    }
}

public sealed class CleanupItemViewModel : ObservableObject
{
    private readonly Action _onChanged;
    private bool _isSelected;

    public CleanupItemViewModel(CleanupTarget target, Action onChanged)
    {
        Target = target;
        _onChanged = onChanged;
        _isSelected = CanSelect && target.Info.TickedByDefault;
    }

    public CleanupTarget Target { get; }

    public string Name => Target.Info.Name;

    public string Description => Target.Info.Description;

    public string SizeText => Target.Category switch
    {
        CleanupCategory.WindowsUpdate when Target.Bytes == 0 => "Size varies",
        CleanupCategory.WindowsUpdate => $"At least {ByteSize.Format(Target.Bytes)}",
        _ when Target.Bytes == 0 => "Nothing to clear",
        _ => ByteSize.Format(Target.Bytes),
    };

    /// <summary>
    /// Can't tick a browser that's open, or something that's already empty. Windows Update can
    /// always be ticked, because Windows only finds out how much it frees by doing it.
    /// </summary>
    public bool CanSelect => !Target.IsBlocked && (Target.Bytes > 0 || Target.Category == CleanupCategory.WindowsUpdate);

    public string? BlockedReason => Target.BlockedReason;

    public bool IsBlocked => Target.IsBlocked;

    public bool IsRecycleBin => Target.Category == CleanupCategory.RecycleBin;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect)
            {
                value = false;
            }

            if (SetProperty(ref _isSelected, value))
            {
                _onChanged();
            }
        }
    }
}
