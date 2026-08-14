using GH_Net.WPF.Localization;
using GH_Net.WPF.ViewModels;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;

namespace GH_Net.WPF.Views;

public partial class CreateJobWindow : Window
{
    private readonly CreateJobViewModel _viewModel;
    private readonly LanguageService _language;
    public CreateJobWindow(CreateJobViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _language = App.GetService<LanguageService>();
        DataContext = viewModel;
        Closing += OnClosing;
    }
    private void GenerateName_Click(object sender, RoutedEventArgs e) => _viewModel.GenerateAutomaticName();
    private async void ChooseProduct_Click(object sender, RoutedEventArgs e) => await ChooseCsvAsync(_viewModel.Products);
    private async void ChooseCarton_Click(object sender, RoutedEventArgs e) => await ChooseCsvAsync(_viewModel.Cartons);
    private async void ChoosePallet_Click(object sender, RoutedEventArgs e) => await ChooseCsvAsync(_viewModel.Pallets);
    private async Task ChooseCsvAsync(CsvSelectionViewModel target)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try { await target.LoadAsync(dialog.FileName); }
        catch (Exception exception) { AppMessageWindow.Show(this, exception.Message, _language.Get("Create.FailedTitle"), AppMessageKind.Warning); }
    }
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var result = await _viewModel.CreateAsync();
        if (result.Job is null) { AppMessageWindow.Show(this, result.Error ?? string.Empty, _language.Get("Create.FailedTitle"), AppMessageKind.Warning); return; }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (!_viewModel.IsBusy) DialogResult = false; }
    private void OnClosing(object? sender, CancelEventArgs e) { if (_viewModel.IsBusy) e.Cancel = true; }
}
