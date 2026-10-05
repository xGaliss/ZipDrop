using System.Globalization;
using System.Windows.Data;
using ZipDrop.Core.Baskets;
using ZipDrop.Services;

namespace ZipDrop.UI;

/// <summary>BasketItem -> Explorer icon for its type. ConverterParameter: "Small" | "Large" | "ExtraLarge".</summary>
internal sealed class ItemIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BasketItem item) return null;
        var size = Enum.TryParse<ShellIcons.Size>(parameter as string, out var s) ? s : ShellIcons.Size.Large;
        return ShellIcons.For(item, size);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
