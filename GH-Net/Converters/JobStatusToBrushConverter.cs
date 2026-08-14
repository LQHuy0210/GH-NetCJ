using GH_Net.Core.Enums;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GH_Net.WPF.Converters;

/// <summary>Converts a job status to a light-theme badge brush.</summary>
public sealed class JobStatusToBrushConverter : IValueConverter
{
    private static readonly Brush NewBrush = Create("#687386");
    private static readonly Brush RunningBrush = Create("#0077B6");
    private static readonly Brush PausedBrush = Create("#D97706");
    private static readonly Brush CompletedBrush = Create("#218A4A");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is JobStatus status
            ? status switch
            {
                JobStatus.NewlyCreated => NewBrush,
                JobStatus.Running => RunningBrush,
                JobStatus.Paused => PausedBrush,
                JobStatus.Completed => CompletedBrush,
                _ => NewBrush,
            }
            : NewBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static Brush Create(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
