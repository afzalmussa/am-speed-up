using System.Windows.Threading;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Services;

/// <summary>
/// Shows the action runner's safety questions as windows. The runner works in the background, so
/// each question is passed to the UI thread and the runner waits for the answer.
/// </summary>
public sealed class UiSafetyPrompts(IDialogService dialogs, Dispatcher dispatcher) : ISafetyPrompts
{
    public Task<bool> ContinueWithoutRestorePointAsync(RestorePointResult result, CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(() => dialogs.AskContinueWithoutRestorePoint(result), DispatcherPriority.Normal, cancellationToken).Task;

    public Task WarnAboutAntivirusAsync(AntivirusCheckResult result, CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(() => dialogs.ShowAntivirusWarning(result), DispatcherPriority.Normal, cancellationToken).Task;
}
