using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GH_Net.WPF.Converters;

/// <summary>Đổi trạng thái kết nối sang màu trạng thái của light theme.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var resources = System.Windows.Application.Current?.Resources;
        var key = value is true ? "SuccessBrush" : "DangerBrush";
        return resources?[key] as Brush
               ?? (value is true ? Brushes.SeaGreen : Brushes.Crimson);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
        => throw new NotSupportedException();
}
