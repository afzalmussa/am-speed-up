using System.Runtime.InteropServices;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Windows.Cleanup;

/// <summary>
/// Moves files to the Recycle Bin with Windows' own file operation. If a file can't go to the
/// Recycle Bin (it's too big, for example), Windows asks before deleting it for good, instead of
/// doing it silently. The user can say no.
/// </summary>
public sealed class RecycleExecutor(IAppLog log) : IActionExecutor
{
    private const uint Delete = 0x0003;
    private const ushort AllowUndo = 0x0040;
    private const ushort NoConfirmation = 0x0010;
    private const ushort Silent = 0x0004;
    private const ushort NoErrorUi = 0x0400;
    private const ushort WantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref FileOperation operation);

    public bool CanRun(PlannedAction action) => action is RecycleFileAction;

    public Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var file = ((RecycleFileAction)action).File;
        if (action.CheckProtection() is { IsProtected: true } protection)
        {
            return Task.FromResult(new ActionOutcome(action, OutcomeStatus.Skipped, protection.Reason!));
        }

        if (!File.Exists(file.Path))
        {
            return Task.FromResult(new ActionOutcome(action, OutcomeStatus.Skipped, "It's already gone."));
        }

        var operation = new FileOperation
        {
            Function = Delete,
            From = file.Path + "\0\0",
            Flags = (ushort)(AllowUndo | NoConfirmation | WantNukeWarning | Silent | NoErrorUi),
        };

        var result = SHFileOperation(ref operation);
        log.Info($"Move to Recycle Bin of {file.Path}: result {result}, cancelled {operation.AnyOperationsAborted}.");

        if (File.Exists(file.Path))
        {
            return Task.FromResult(operation.AnyOperationsAborted
                ? new ActionOutcome(action, OutcomeStatus.Skipped, "Not moved. It couldn't go to the Recycle Bin and you chose to keep it.")
                : new ActionOutcome(action, OutcomeStatus.Failed, "Windows couldn't move it. It may be open in another program."));
        }

        return Task.FromResult(new ActionOutcome(action, OutcomeStatus.Succeeded, ""));
    }
}
