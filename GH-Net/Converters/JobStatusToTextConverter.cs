using GH_Net.Core.Enums;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GH_Net.WPF.Converters;

/// <summary>Converts a job status using the active language dictionary.</summary>
public sealed class JobStatusToTextConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
        => value is JobStatus status
            ? status switch
            {
                JobStatus.NewlyCreated => Text("Status.NewlyCreated"),
                JobStatus.Paused => Text("Status.Paused"),
                JobStatus.Running => Text("Status.Running"),
                JobStatus.Completed => Text("Status.Completed"),
                _ => status.ToString(),
            }
            : string.Empty;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
        => Binding.DoNothing;

    private static string Text(string key)
        => Application.Current?.TryFindResource(key) as string ?? key;
}
