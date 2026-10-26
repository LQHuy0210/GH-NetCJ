using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Windows;

namespace GH_Net.WPF.Views;

public partial class TechnicalAccessWindow : Window
{
    private readonly ITechnicalSettingsAccess _access;
    private readonly LanguageService _language;
    private readonly bool _setup;
    private bool _busy;
    public TechnicalAccessWindow(ITechnicalSettingsAccess access, LanguageService language, bool setup)
    {
        InitializeComponent(); _access = access; _language = language; _setup = setup;
        Heading.Text = language.Get(setup ? "PlcTech.Setup" : "PlcTech.Unlock");
        Description.Text = language.Get(setup ? "PlcTech.SetupHint" : "PlcTech.UnlockHint");
        Confirmation.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => CodeBox.Focus();
        Closing += (_, args) => { if (_busy) args.Cancel = true; };
        Closed += (_, _) => { CodeBox.Clear(); ConfirmBox.Clear(); };
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_setup && (CodeBox.Password.Length is < 8 or > 64 || string.IsNullOrWhiteSpace(CodeBox.Password)))
        { ErrorText.Text = _language.Get("PlcTech.CodeLength"); return; }
        if (_setup && CodeBox.Password != ConfirmBox.Password)
        { ErrorText.Text = _language.Get("PlcTech.Mismatch"); return; }
        _busy = true; Submit.IsEnabled = false;
        try
        {
            bool unlocked;
            if (_setup) { await _access.ConfigureAsync(CodeBox.Password); unlocked = true; }
            else
            {
                var result = await _access.UnlockAsync(CodeBox.Password); unlocked = result.Succeeded;
                if (!unlocked) ErrorText.Text = result.RetryAfterSeconds > 0
                    ? _language.Format("PlcTech.Retry", result.RetryAfterSeconds) : _language.Get("PlcTech.Incorrect");
            }
            CodeBox.Clear(); ConfirmBox.Clear();
            if (unlocked) { _busy = false; DialogResult = true; }
        }
        catch { ErrorText.Text = _language.Get("PlcTech.StorageError"); }
        finally { _busy = false; Submit.IsEnabled = true; }
    }
}
