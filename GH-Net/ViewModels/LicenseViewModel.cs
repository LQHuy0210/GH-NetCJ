using GH_Net.Licensing;
using GH_Net.Service.Abstractions;
using GH_Net.WPF.Localization;
using System.Windows;

namespace GH_Net.WPF.ViewModels;

/// <summary>Activation, renewal and effective license details.</summary>
public sealed class LicenseViewModel : ViewModelBase, IDisposable
{
    private readonly ILicenseManager _licenseManager;
    private readonly LanguageService _language;
    private string _tokenInput = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _disposed;

    public LicenseViewModel(ILicenseManager licenseManager, LanguageService language)
    {
        _licenseManager = licenseManager;
        _language = language;
        ActivateCommand = new AsyncRelayCommand(ActivateAsync, CanActivate);
        _licenseManager.LicenseChanged += OnLicenseChanged;
        _language.LanguageChanged += OnLanguageChanged;
    }

    public AsyncRelayCommand ActivateCommand { get; }
    public event EventHandler? ActivationSucceeded;

    public string TokenInput
    {
        get => _tokenInput;
        set
        {
            if (SetProperty(ref _tokenInput, value))
                ActivateCommand.NotifyCanExecuteChanged();
        }
    }

    public string HardwareId => _licenseManager.Current.HardwareId;
    public string Customer => _licenseManager.Current.Customer;
    public bool HasCustomer => !string.IsNullOrWhiteSpace(Customer);
    public bool IsValid => _licenseManager.Current.IsValid;
    public string StatusText => _language.Get($"License.Status.{_licenseManager.Current.Status}");
    public string ExpiryText => _licenseManager.Current.ExpiresAtUtc is { } expiry
        ? expiry.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        : IsValid ? _language.Get("License.Perpetual") : "—";
    public string FeaturesText => IsValid
        ? string.Join(", ", Enum.GetValues<LicenseFeature>()
            .Where(feature => feature is not LicenseFeature.None and not LicenseFeature.All &&
                              _licenseManager.Current.HasFeature(feature)))
        : "—";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                ActivateCommand.NotifyCanExecuteChanged();
        }
    }

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    private bool CanActivate()
        => !IsBusy && !string.IsNullOrWhiteSpace(TokenInput);

    private async Task ActivateAsync()
    {
        IsBusy = true;
        Message = string.Empty;
        try
        {
            LicenseValidationResult result = await _licenseManager
                .ActivateAsync(TokenInput)
                .ConfigureAwait(true);
            if (!result.IsValid)
            {
                Message = _language.Get($"License.Status.{result.Status}");
                return;
            }

            TokenInput = string.Empty;
            Message = _language.Get("License.Activated");
            RefreshSnapshot();
            ActivationSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            Message = _language.Get("License.ActivationFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLicenseChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            RefreshSnapshot();
        else
            _ = dispatcher.InvokeAsync(RefreshSnapshot);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshSnapshot();

    private void RefreshSnapshot()
    {
        OnPropertyChanged(nameof(HardwareId));
        OnPropertyChanged(nameof(Customer));
        OnPropertyChanged(nameof(HasCustomer));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ExpiryText));
        OnPropertyChanged(nameof(FeaturesText));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _licenseManager.LicenseChanged -= OnLicenseChanged;
        _language.LanguageChanged -= OnLanguageChanged;
    }
}
