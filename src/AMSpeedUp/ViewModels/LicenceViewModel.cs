using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Licensing;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMSpeedUp.ViewModels;

/// <summary>The licence window: enter a key, check it, remove this PC, or buy Pro.</summary>
public sealed partial class LicenceViewModel : ObservableObject
{
    private readonly LicenceManager _licence;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shell;
    private readonly AppSettings _settings;

    private string _keyText = "";
    private string? _message;
    private bool _messageIsGood;
    private bool _isBusy;

    public LicenceViewModel(LicenceManager licence, IDialogService dialogs, IShellLauncher shell, AppSettings settings)
    {
        _licence = licence;
        _dialogs = dialogs;
        _shell = shell;
        _settings = settings;
        _licence.Changed += Refresh;
    }

    public LicenceStatus Status => _licence.Current;

    public string Badge => LicenceText.Badge(Status);

    public string Description => LicenceText.Describe(Status);

    public string? ActivationsText => LicenceText.Activations(Status.Licence);

    public string? LastCheckedText => Status.Licence is null ? null : LicenceText.LastChecked(Status.Licence);

    public string? MaskedKey => Status.Licence is null ? null : $"Licence key: {LicenceText.Mask(Status.Licence.Key)}";

    /// <summary>The key box shows whenever there's no working licence on this PC.</summary>
    public bool ShowKeyEntry => Status.State is LicenceState.Free or LicenceState.Expired or LicenceState.Revoked;

    /// <summary>"Check now" and "Remove this PC" show whenever there's a saved licence.</summary>
    public bool ShowManage => Status.Licence is not null && Status.State != LicenceState.DeveloperMode;

    public bool CanBuy => _settings.HasCheckoutUrl && !Status.IsPro;

    public string KeyText
    {
        get => _keyText;
        set
        {
            if (SetProperty(ref _keyText, value))
            {
                ActivateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>Green for good news, yellow for problems.</summary>
    public bool MessageIsGood
    {
        get => _messageIsGood;
        private set => SetProperty(ref _messageIsGood, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ActivateCommand.NotifyCanExecuteChanged();
                CheckNowCommand.NotifyCanExecuteChanged();
                RemoveThisPcCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private async Task ActivateAsync()
    {
        IsBusy = true;
        Message = "Checking your licence key...";
        MessageIsGood = true;
        try
        {
            var result = await _licence.ActivateAsync(KeyText).ConfigureAwait(true);
            Show(result);
            if (result.Succeeded)
            {
                KeyText = "";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanActivate() => !IsBusy && !string.IsNullOrWhiteSpace(KeyText);

    [RelayCommand(CanExecute = nameof(CanUseOnline))]
    private async Task CheckNowAsync()
    {
        IsBusy = true;
        Message = "Checking your licence...";
        MessageIsGood = true;
        try
        {
            Show(await _licence.CheckNowAsync().ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseOnline))]
    private async Task RemoveThisPcAsync()
    {
        var sure = _dialogs.Confirm(
            "Remove Pro from this PC?",
            "This PC will go back to the free version, and the space on your licence can be used on another PC. You can enter your key again here later if you change your mind.",
            "Remove this PC",
            "Keep Pro here");
        if (!sure)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Show(await _licence.DeactivateAsync().ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUseOnline() => !IsBusy;

    [RelayCommand]
    private void GetPro()
    {
        if (_settings.HasCheckoutUrl && !_shell.OpenUrl(_settings.CheckoutUrl!))
        {
            _dialogs.ShowMessage("Couldn't open your web browser", $"You can buy AM Speed Up Pro here:\n\n{_settings.CheckoutUrl}");
        }
    }

    private void Show(LicenceResult result)
    {
        Message = result.Message;
        MessageIsGood = result.Succeeded;
    }

    private void Refresh()
    {
        foreach (var name in new[] { nameof(Status), nameof(Badge), nameof(Description), nameof(ActivationsText), nameof(LastCheckedText),
                                     nameof(MaskedKey), nameof(ShowKeyEntry), nameof(ShowManage), nameof(CanBuy) })
        {
            OnPropertyChanged(name);
        }
    }
}
