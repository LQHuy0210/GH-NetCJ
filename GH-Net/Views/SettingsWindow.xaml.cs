using GH_Net.WPF.ViewModels;
using System.Windows;
using System.ComponentModel;

namespace GH_Net.WPF.Views;

/// <summary>Interaction logic for the application settings window.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly System.Windows.Threading.DispatcherTimer _technicalTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _technicalTimer.Tick += (_, _) => _viewModel.RefreshTechnicalAccess();
        SettingsTabs.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, SettingsTabs) && SettingsTabs.SelectedItem != PlcSettingsTab)
                _viewModel.LockTechnicalSettings();
        };
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => { _technicalTimer.Stop(); _viewModel.Dispose(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
        _technicalTimer.Start();
    }

    private async void TechnicalAccess_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var access = App.GetService<GH_Net.Service.Abstractions.ITechnicalSettingsAccess>();
            var language = App.GetService<GH_Net.WPF.Localization.LanguageService>();
            bool setup = !await access.IsConfiguredAsync();
            var dialog = new TechnicalAccessWindow(access, language, setup) { Owner = this };
            if (dialog.ShowDialog() == true) await _viewModel.ReloadTechnicalSettingsAsync();
        }
        catch
        {
            _viewModel.LockTechnicalSettings();
            AppMessageWindow.Show(this, App.GetService<GH_Net.WPF.Localization.LanguageService>().Get("PlcTech.StorageError"), Title, AppMessageKind.Error);
        }
    }

    private void LockTechnical_Click(object sender, RoutedEventArgs e) => _viewModel.LockTechnicalSettings();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsTabs.SelectedContent is DependencyObject content && HasValidationError(content))
        {
            AppMessageWindow.Show(this, App.GetService<GH_Net.WPF.Localization.LanguageService>().Get("Automation.InvalidInput"), Title, AppMessageKind.Warning);
            return;
        }
        AsyncRelayCommand command = SettingsTabs.SelectedIndex switch
        {
            0 => _viewModel.ApplyPrinterCommand,
            1 => _viewModel.ApplyScannerCommand,
            2 => _viewModel.ApplyCameraCommand,
            _ => _viewModel.ApplyPlcCommand
        };
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private static bool HasValidationError(DependencyObject element)
    {
        if (System.Windows.Controls.Validation.GetHasError(element)) return true;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            if (HasValidationError(System.Windows.Media.VisualTreeHelper.GetChild(element, i))) return true;
        return false;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
            e.Cancel = true;
    }
}
