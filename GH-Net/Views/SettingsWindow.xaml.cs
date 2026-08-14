using GH_Net.WPF.ViewModels;
using System.Windows;
using System.ComponentModel;

namespace GH_Net.WPF.Views;

/// <summary>Interaction logic for the application settings window.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        AsyncRelayCommand command = SettingsTabs.SelectedIndex == 0
            ? _viewModel.ApplyPrinterCommand
            : _viewModel.ApplyScannerCommand;
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
            e.Cancel = true;
    }
}
