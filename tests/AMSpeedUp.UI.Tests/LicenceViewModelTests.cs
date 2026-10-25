using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Licensing;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

internal sealed class ScriptedLicenceService : ILicenceService
{
    public LicenceResponse Activate { get; set; } = new(LicenceOutcome.Valid, null, new LicenceInfo { InstanceId = "i-1", ActivationLimit = 3, ActivationUsage = 1 });

    public LicenceResponse Validate { get; set; } = new(LicenceOutcome.Valid, null, new LicenceInfo { ActivationLimit = 3, ActivationUsage = 1 });

    public LicenceResponse Deactivate { get; set; } = new(LicenceOutcome.Valid);

    public int Deactivations { get; private set; }

    public Task<LicenceResponse> ActivateAsync(string key, string instanceName, CancellationToken cancellationToken = default) => Task.FromResult(Activate);

    public Task<LicenceResponse> ValidateAsync(string key, string instanceId, CancellationToken cancellationToken = default) => Task.FromResult(Validate);

    public Task<LicenceResponse> DeactivateAsync(string key, string instanceId, CancellationToken cancellationToken = default)
    {
        Deactivations++;
        return Task.FromResult(Deactivate);
    }
}

internal sealed class MemoryStore : ILicenceStore
{
    public StoredLicence? Saved { get; set; }

    public StoredLicence? Load() => Saved;

    public void Save(StoredLicence licence) => Saved = licence;

    public void Delete() => Saved = null;
}

public class LicenceViewModelTests
{
    private const string Key = "38B1460A-5104-4067-A91D-77B872934D51";

    private readonly ScriptedLicenceService _service = new();
    private readonly MemoryStore _store = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeShell _shell = new();
    private readonly AppSettings _settings = new() { CheckoutUrl = "https://am-software.lemonsqueezy.com/checkout/buy/abc" };

    private (LicenceViewModel Vm, LicenceManager Manager, MainViewModel Main) Create(bool developerMode = false)
    {
        var manager = new LicenceManager(_service, _store, new FakeLog(), developerMode);
        var main = new MainViewModel(new FakeLog(), _shell, _dialogs, _settings, AppOptions.Default, licence: manager);
        return (new LicenceViewModel(manager, _dialogs, _shell, _settings), manager, main);
    }

    [Fact]
    public void The_free_version_shows_the_key_box_and_a_way_to_buy()
    {
        var (vm, _, main) = Create();

        Assert.True(vm.ShowKeyEntry);
        Assert.False(vm.ShowManage);
        Assert.True(vm.CanBuy);
        Assert.Equal("Free version", vm.Badge);
        Assert.Equal("Free version", main.LicenceStatusText);
        Assert.True(main.CanGetPro);
    }

    [Fact]
    public async Task Activating_switches_the_whole_app_to_Pro()
    {
        var (vm, manager, main) = Create();
        vm.KeyText = Key;

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.True(manager.Current.IsPro);
        Assert.True(vm.MessageIsGood);
        Assert.Equal(LicenceText.Activated, vm.Message);
        Assert.Equal("", vm.KeyText);
        Assert.False(vm.ShowKeyEntry);
        Assert.True(vm.ShowManage);
        Assert.False(vm.CanBuy);
        Assert.Equal("Used on 1 of 3 PCs.", vm.ActivationsText);
        Assert.Equal("Licence key: 38B1...4D51", vm.MaskedKey);
        Assert.Equal("Pro", main.LicenceStatusText);
        Assert.True(main.IsPro);
        Assert.False(main.CanGetPro);
    }

    [Fact]
    public async Task A_rejected_key_explains_why_in_a_warning()
    {
        _service.Activate = new LicenceResponse(LicenceOutcome.LimitReached);
        var (vm, manager, _) = Create();
        vm.KeyText = Key;

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.False(manager.Current.IsPro);
        Assert.False(vm.MessageIsGood);
        Assert.Contains("Remove this PC", vm.Message);
        Assert.Equal(Key, vm.KeyText);
    }

    [Fact]
    public void Activate_needs_something_typed_in()
    {
        var (vm, _, _) = Create();

        Assert.False(vm.ActivateCommand.CanExecute(null));
        vm.KeyText = Key;
        Assert.True(vm.ActivateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Removing_this_PC_asks_first_and_does_nothing_if_the_answer_is_no()
    {
        var (vm, manager, _) = Create();
        vm.KeyText = Key;
        await vm.ActivateCommand.ExecuteAsync(null);
        _dialogs.ConfirmAnswerForQuestions = false;

        await vm.RemoveThisPcCommand.ExecuteAsync(null);

        Assert.Equal(["Remove Pro from this PC?"], _dialogs.Questions);
        Assert.Equal(0, _service.Deactivations);
        Assert.True(manager.Current.IsPro);
    }

    [Fact]
    public async Task Removing_this_PC_returns_it_to_free()
    {
        var (vm, manager, main) = Create();
        vm.KeyText = Key;
        await vm.ActivateCommand.ExecuteAsync(null);

        await vm.RemoveThisPcCommand.ExecuteAsync(null);

        Assert.Equal(1, _service.Deactivations);
        Assert.False(manager.Current.IsPro);
        Assert.Equal(LicenceText.Removed, vm.Message);
        Assert.True(vm.ShowKeyEntry);
        Assert.Equal("Free version", main.LicenceStatusText);
    }

    [Fact]
    public async Task Check_now_reports_the_result()
    {
        var (vm, _, _) = Create();
        vm.KeyText = Key;
        await vm.ActivateCommand.ExecuteAsync(null);
        _service.Validate = new LicenceResponse(LicenceOutcome.NoAnswer);

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.False(vm.MessageIsGood);
        Assert.Contains("Pro is switched on", vm.Message);
    }

    [Fact]
    public void Get_Pro_opens_the_checkout_page()
    {
        var (vm, _, _) = Create();

        vm.GetProCommand.Execute(null);

        Assert.Equal([_settings.CheckoutUrl!], _shell.OpenedUrls);
    }

    [Fact]
    public void Developer_mode_shows_Pro_without_manage_buttons()
    {
        var (vm, _, main) = Create(developerMode: true);

        Assert.Equal("Pro (developer mode)", vm.Badge);
        Assert.False(vm.ShowKeyEntry);
        Assert.False(vm.ShowManage);
        Assert.True(main.IsPro);
    }

    [Fact]
    public void Licence_window_renders_for_free_and_Pro() => WpfHost.Run(() =>
    {
        var (free, _, _) = Create();
        var window = new LicenceWindow { DataContext = free };
        Assert.True(Screenshots.HasContent(Screenshots.Render(window, "licence-free", 600)));
        window.Close();

        _store.Saved = new StoredLicence
        {
            Key = Key,
            InstanceId = "i-1",
            ActivatedAt = DateTimeOffset.Now.AddDays(-3),
            LastConfirmedAt = DateTimeOffset.Now.AddDays(-1),
            ActivationLimit = 3,
            ActivationUsage = 2,
        };
        var (pro, _, _) = Create();
        window = new LicenceWindow { DataContext = pro };
        Assert.True(Screenshots.HasContent(Screenshots.Render(window, "licence-pro", 600)));
        window.Close();
    });

    [Fact]
    public void Header_shows_Get_Pro_on_the_free_version() => WpfHost.Run(() =>
    {
        var (_, _, main) = Create();
        var window = new MainWindow { DataContext = main };

        Assert.True(Screenshots.HasContent(Screenshots.Render(window, "main-window-free-with-get-pro", 1040, 680)));
        window.Close();
    });
}
