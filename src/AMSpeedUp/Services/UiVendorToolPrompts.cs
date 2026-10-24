using System.Windows.Threading;
using AMSpeedUp.Core.VendorTools;

namespace AMSpeedUp.Services;

/// <summary>Shows the vendor removal tool messages as windows, from the background run.</summary>
public sealed class UiVendorToolPrompts(IDialogService dialogs, Dispatcher dispatcher) : IVendorToolPrompts
{
    public Task<bool> ReadyToRunAsync(VendorToolInfo tool, CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(() => dialogs.ConfirmVendorTool(tool), DispatcherPriority.Normal, cancellationToken).Task;

    public Task ShowCannotRunAsync(VendorToolInfo tool, string reason, CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(() => dialogs.ShowVendorToolProblem(tool, reason), DispatcherPriority.Normal, cancellationToken).Task;
}
