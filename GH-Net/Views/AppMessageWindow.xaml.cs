using System.Windows;
using System.Windows.Media;

namespace GH_Net.WPF.Views;

public enum AppMessageKind
{
    Information,
    Warning,
    Error,
}

public partial class AppMessageWindow : Window
{
    private AppMessageWindow(
        string message,
        string title,
        AppMessageKind kind)
    {
        InitializeComponent();

        Title = title;
        HeadingText.Text = title;
        MessageText.Text = message;
        ApplyKind(kind);
    }

    public static void Show(
        Window? owner,
        string message,
        string title,
        AppMessageKind kind = AppMessageKind.Information)
    {
        var dialog = new AppMessageWindow(message, title, kind);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.ShowDialog();
    }

    private void ApplyKind(AppMessageKind kind)
    {
        var (background, foreground, asset) = kind switch
        {
            AppMessageKind.Error => ("#FDEBEC", "DangerBrush", "fail.svg"),
            AppMessageKind.Warning => ("#FFF4E5", "WarningBrush", "error.svg"),
            _ => ("#E7F2FA", "PrimaryBrush", "point.svg"),
        };

        IconBackground.Background =
            (Brush)new BrushConverter().ConvertFromString(background)!;
        var iconBrush = (Brush)FindResource(foreground);
        MessageIcon.Fill = iconBrush;
        MessageIcon.Stroke = iconBrush;
        MessageIcon.UriSource = new Uri(
            $"pack://application:,,,/GH-Net;component/Assets/{asset}",
            UriKind.Absolute);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;
}
