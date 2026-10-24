using System.Runtime.InteropServices;

namespace AMSpeedUp.Windows.Cleanup;

/// <summary>Asks Windows how much is in the Recycle Bin, and empties it, for all drives.</summary>
public static class RecycleBin
{
    private const uint NoConfirmation = 0x1;
    private const uint NoProgressWindow = 0x2;
    private const uint NoSound = 0x4;

    // Windows returns this when the Recycle Bin is already empty.
    private const int AlreadyEmpty = unchecked((int)0x8000FFFF);

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
    private static extern int SHQueryRecycleBin(string? rootPath, ref QueryInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")]
    private static extern int SHEmptyRecycleBin(IntPtr window, string? rootPath, uint flags);

    /// <summary>Total size and number of items, or null if Windows couldn't say.</summary>
    public static (long Bytes, long Items)? Query()
    {
        var info = new QueryInfo { Size = Marshal.SizeOf<QueryInfo>() };
        var result = SHQueryRecycleBin(null, ref info);
        return result == 0 ? (info.Bytes, info.Items) : null;
    }

    /// <summary>Empties it without Windows' own "are you sure" box: the user already confirmed in the preview.</summary>
    public static int Empty()
    {
        var result = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressWindow | NoSound);
        return result == AlreadyEmpty ? 0 : result;
    }
}
