using System.Globalization;
using System.Windows.Data;
using ZipDrop.Core.Formatting;

namespace ZipDrop.UI;

/// <summary>long? bytes -> "143 MB"; null (still measuring) -> "…".</summary>
internal sealed class SizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? SizeFormatter.Format(bytes, Strings.Culture) : "…";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
