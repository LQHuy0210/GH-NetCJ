using GH_Net.WPF.ViewModels;
using System.Windows;

namespace GH_Net.WPF.Views;

public partial class LicenseWindow : Window
{
    private readonly LicenseViewModel _viewModel;
    private readonly bool _activationRequired;

    public LicenseWindow(LicenseViewModel viewModel, bool activationRequired)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _activationRequired = activationRequired;
        DataContext = viewModel;
        _viewModel.ActivationSucceeded += OnActivationSucceeded;
        Closed += OnClosed;
        if (activationRequired)
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void CopyHardwareId_Click(object sender, RoutedEventArgs e)
        => Clipboard.SetText(_viewModel.HardwareId);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnActivationSucceeded(object? sender, EventArgs e)
    {
        if (!_activationRequired)
            return;
        DialogResult = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.ActivationSucceeded -= OnActivationSucceeded;
        _viewModel.Dispose();
    }
}
