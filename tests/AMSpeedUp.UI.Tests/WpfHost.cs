using System.Windows;
using System.Windows.Threading;

namespace AMSpeedUp.UI.Tests;

/// <summary>
/// WPF needs one long-lived UI (STA) thread with the app's resources loaded.
/// Every window test runs its code on that thread through <see cref="Run"/>.
/// </summary>
internal static class WpfHost
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(Start);

    public static void Run(Action action) => UiDispatcher.Value.Invoke(action);

    /// <summary>Runs async code on the UI thread, the way the app does, so awaits come back to the UI thread.</summary>
    public static Task RunAsync(Func<Task> action) => UiDispatcher.Value.InvokeAsync(action).Task.Unwrap();

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            var app = new AMSpeedUp.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test UI thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
