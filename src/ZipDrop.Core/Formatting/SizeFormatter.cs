using System.Globalization;

namespace ZipDrop.Core.Formatting;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>"0 B", "812 KB", "143 MB", "1.4 GB". Decimal only below 10 units.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024) return $"{Math.Max(0, bytes)} B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = value < 10 ? "0.#" : "0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    public static string Items(int count) => count == 1 ? "1 item" : $"{count} items";
}
