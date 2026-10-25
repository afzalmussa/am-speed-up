using System.Collections.ObjectModel;
using System.Globalization;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

/// <summary>
/// Pro: finds large files and duplicates in the user's own folders. Nothing is ticked for them:
/// the user chooses each file, and files only ever go to the Recycle Bin.
/// </summary>
public sealed partial class LargeFilesViewModel : ObservableObject
{
    public const string FeatureName = "The large file and duplicate finder";

    private readonly Func<IReadOnlyList<string>> _folders;
    private readonly ActionRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shell;
    private readonly IAppLog _log;
    private readonly AppOptions _options;
    private readonly Func<bool> _isPro;

    private IReadOnlyList<string> _searchedFolders = [];
    private IReadOnlyList<DuplicateGroup> _duplicateGroups = [];
    private bool _isBusy;
    private bool _hasSearched;
    private string _busyText = "";
    private IReadOnlyList<string> _summary = [];

    public LargeFilesViewModel(Func<IReadOnlyList<string>> folders, ActionRunner runner, IDialogService dialogs, IShellLauncher shell, IAppLog log, AppOptions options, Func<bool> isPro)
    {
        _folders = folders;
        _runner = runner;
        _dialogs = dialogs;
        _shell = shell;
        _log = log;
        _options = options;
        _isPro = isPro;
    }

    public ObservableCollection<FoundFileViewModel> LargeFiles { get; } = [];

    public ObservableCollection<DuplicateGroupViewModel> Duplicates { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SearchCommand.NotifyCanExecuteChanged();
                RecycleSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSearched
    {
        get => _hasSearched;
        private set
        {
            if (SetProperty(ref _hasSearched, value))
            {
                OnPropertyChanged(nameof(SearchButtonText));
                OnPropertyChanged(nameof(ShowNothingFound));
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

    public bool HasLargeFiles => LargeFiles.Count > 0;

    public bool HasDuplicates => Duplicates.Count > 0;

    public bool ShowNothingFound => HasSearched && !HasLargeFiles && !HasDuplicates;

    public string SearchButtonText => HasSearched ? "Look again" : "Look for large and duplicate files";

    private IEnumerable<FoundFileViewModel> AllFiles => LargeFiles.Concat(Duplicates.SelectMany(d => d.Files));

    public IReadOnlyList<FoundFileViewModel> Selected => AllFiles.Where(f => f.IsSelected).DistinctBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase).ToList();

    public string RecycleButtonText => Selected switch
    {
        [] => "Move to the Recycle Bin",
        [var one] => $"Move 1 file to the Recycle Bin ({ByteSize.Format(one.File.Bytes)})",
        var many => $"Move {many.Count} files to the Recycle Bin ({ByteSize.Format(many.Sum(f => f.File.Bytes))})",
    };

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (!_isPro())
        {
            _dialogs.ShowProRequired(FeatureName);
            return;
        }

        IsBusy = true;
        BusyText = "Looking through your folders. This can take a few minutes...";
        try
        {
            _searchedFolders = _folders();
            var progress = new Progress<string>(message => BusyText = message);
            var result = await Task.Run(() => FileFinder.Search(_searchedFolders, progress)).ConfigureAwait(true);
            _log.Info($"Large file search looked at {result.FilesLookedAt} file(s) in {string.Join(", ", _searchedFolders)}: {result.Large.Count} large, {result.Duplicates.Count} duplicate group(s).");

            LargeFiles.Clear();
            foreach (var file in result.Large)
            {
                LargeFiles.Add(new FoundFileViewModel(file, OnSelectionChanged, ShowInFolder));
            }

            _duplicateGroups = result.Duplicates;
            Duplicates.Clear();
            foreach (var group in result.Duplicates)
            {
                Duplicates.Add(new DuplicateGroupViewModel(group, OnSelectionChanged, ShowInFolder));
            }

            HasSearched = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("The large file search failed.", ex);
            _dialogs.ShowMessage("The search didn't finish", "Something went wrong while looking through your folders. Nothing was changed. The details are in the log.");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasLargeFiles));
            OnPropertyChanged(nameof(HasDuplicates));
            OnPropertyChanged(nameof(ShowNothingFound));
            OnSelectionChanged();
        }
    }

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRecycle))]
    private async Task RecycleSelectedAsync()
    {
        if (!_isPro())
        {
            _dialogs.ShowProRequired(FeatureName);
            return;
        }

        var selected = Selected;
        var paths = selected.Select(f => f.File.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (FileFinder.GroupWithEveryCopySelected(_duplicateGroups, paths) is { } group)
        {
            _dialogs.ShowMessage(
                "Keep at least one copy",
                $"You've ticked every copy of {group.Files[0].Name}, so it would be gone completely. Untick one copy to keep it.");
            return;
        }

        var plan = ActionPlan.Create(
            selected.Count == 1 ? "Move 1 file to the Recycle Bin" : $"Move {selected.Count} files to the Recycle Bin",
            selected.Select(f => new RecycleFileAction(f.File, _searchedFolders)));
        var preview = plan.CreatePreview(_options.DryRun);
        if (!_dialogs.ConfirmPlan(preview))
        {
            return;
        }

        var confirmed = preview.Confirm();
        IsBusy = true;
        BusyText = "Moving files to the Recycle Bin...";
        try
        {
            var report = await Task.Run(() => _runner.RunAsync(confirmed)).ConfigureAwait(true);
            Summary = [.. RunSummary.Describe(report), "Changed your mind? Open the Recycle Bin on your desktop to put files back."];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Moving files to the Recycle Bin stopped with an error.", ex);
            Summary = ["Something went wrong part-way through. The details are in the log."];
        }
        finally
        {
            IsBusy = false;
        }

        await SearchAsync().ConfigureAwait(true);
    }

    private bool CanRecycle() => !IsBusy && Selected.Count > 0;

    private void ShowInFolder(FoundFile file) => _shell.ShowInFolder(file.Path);

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(RecycleButtonText));
        RecycleSelectedCommand.NotifyCanExecuteChanged();
    }
}

public sealed class FoundFileViewModel : ObservableObject
{
    private static readonly CultureInfo UkEnglish = CultureInfo.GetCultureInfo("en-GB");

    private readonly Action _onChanged;
    private bool _isSelected;

    public FoundFileViewModel(FoundFile file, Action onChanged, Action<FoundFile> showInFolder)
    {
        File = file;
        _onChanged = onChanged;
        ShowInFolderCommand = new RelayCommand(() => showInFolder(file));
    }

    public FoundFile File { get; }

    public string Name => File.Name;

    public string Folder => File.Folder;

    public string SizeText => ByteSize.Format(File.Bytes);

    public string DateText => "Last changed " + File.LastWriteUtc.ToLocalTime().ToString("d MMMM yyyy", UkEnglish);

    public IRelayCommand ShowInFolderCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _onChanged();
            }
        }
    }
}

public sealed class DuplicateGroupViewModel(DuplicateGroup group, Action onChanged, Action<FoundFile> showInFolder)
{
    public DuplicateGroup Group { get; } = group;

    public string Heading => $"{Group.Files.Count} copies of {Group.Files[0].Name}, {ByteSize.Format(Group.BytesEach)} each";

    public string SpareText => $"Keeping one copy frees {ByteSize.Format(Group.SpareBytes)}.";

    public IReadOnlyList<FoundFileViewModel> Files { get; } = group.Files.Select(f => new FoundFileViewModel(f, onChanged, showInFolder)).ToList();
}
