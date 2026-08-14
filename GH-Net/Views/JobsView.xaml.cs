using GH_Net.WPF.ViewModels;
using GH_Net.WPF.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GH_Net.WPF.Views;

/// <summary>Interaction logic for JobsView.</summary>
public partial class JobsView : UserControl
{
    private readonly LanguageService _language;

    public JobsView()
    {
        InitializeComponent();
        _language = App.GetService<LanguageService>();
        _language.LanguageChanged += OnLanguageChanged;
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => _language.LanguageChanged -= OnLanguageChanged;
        UpdateColumnHeaders();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => Dispatcher.Invoke(() =>
        {
            UpdateColumnHeaders();
            JobsGrid.Items.Refresh();
        });

    private void UpdateColumnHeaders()
    {
        NameColumn.Header = _language.Get("Jobs.Name");
        StatusColumn.Header = _language.Get("Jobs.Status");
        ProgressColumn.Header = _language.Get("Jobs.Progress");
        CreatedAtColumn.Header = _language.Get("Jobs.CreatedAt");
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not JobsViewModel viewModel)
            return;

        viewModel.RequestDeleteConfirmation = (jobs, deleteOperation) =>
        {
            var language = App.GetService<LanguageService>();
            var dialog = new DeleteJobsConfirmationWindow(
                jobs,
                language,
                deleteOperation)
            {
                Owner = Window.GetWindow(this),
            };
            return dialog.ShowDialog() == true;
        };

    }

    private void JobsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: JobListItemViewModel job } &&
            DataContext is JobsViewModel viewModel)
        {
            viewModel.OpenJobCommand.Execute(job);
        }
    }
}
