using GH_Net.WPF.Localization;
using GH_Net.WPF.ViewModels;
using System.Windows;
using System.Windows.Threading;

namespace GH_Net.WPF.Views;

public partial class DeleteJobsConfirmationWindow : Window
{
    private readonly Func<Task> _deleteOperation;
    private bool _isProcessing;

    public DeleteJobsConfirmationWindow(
        IReadOnlyList<JobListItemViewModel> jobs,
        LanguageService language,
        Func<Task> deleteOperation)
    {
        InitializeComponent();
        _deleteOperation = deleteOperation;

        QuestionText.Text = language.Format(
            "Jobs.DeleteQuestion",
            jobs.Count);
        JobNamesList.ItemsSource = jobs.Select(job => job.Name).ToArray();
        Closing += (_, e) =>
        {
            if (_isProcessing)
                e.Cancel = true;
        };
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
            return;

        _isProcessing = true;
        ErrorText.Visibility = Visibility.Collapsed;
        ProcessingOverlay.Visibility = Visibility.Visible;

        try
        {
            // Cho overlay được render chắc chắn và giữ đủ lâu để người dùng
            // nhận biết ứng dụng đang xử lý, kể cả khi job nhỏ.
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.WhenAll(
                _deleteOperation(),
                Task.Delay(350));

            _isProcessing = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _isProcessing = false;
            ProcessingOverlay.Visibility = Visibility.Collapsed;
            ErrorText.Text = ex.GetBaseException().Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
