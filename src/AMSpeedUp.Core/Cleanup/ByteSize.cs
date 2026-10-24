using System.Globalization;

namespace AMSpeedUp.Core.Cleanup;

public static class ByteSize
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB"];

    /// <summary>Readable sizes like "850 KB", "1.2 GB" or "12 GB", using 1 KB = 1,024 bytes like File Explorer.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes == 1 ? "1 byte" : $"{Math.Max(bytes, 0)} bytes";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // One decimal place below 10, whole numbers above, so the number stays easy to read.
        var text = value < 10
            ? value.ToString("0.#", CultureInfo.InvariantCulture)
            : value.ToString("0", CultureInfo.InvariantCulture);
        return $"{text} {Units[unit]}";
    }
}
