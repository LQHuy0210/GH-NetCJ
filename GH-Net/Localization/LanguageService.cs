using System.Globalization;
using System.IO;
using System.Windows;

namespace GH_Net.WPF.Localization;

public sealed class LanguageService
{
    private const string Vietnamese = "vi-VN";
    private const string English = "en-US";
    private readonly string _preferenceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GH-Net",
        "language.txt");
    private ResourceDictionary? _activeDictionary;

    public event EventHandler? LanguageChanged;

    public string CurrentCulture { get; private set; } = Vietnamese;
    public string CurrentCode => CurrentCulture == English ? "EN" : "VI";
    public string ToggleToolTip => CurrentCulture == English
        ? "Chuyển sang tiếng Việt"
        : "Switch to English";

    public void Initialize()
    {
        var saved = File.Exists(_preferenceFile)
            ? File.ReadAllText(_preferenceFile).Trim()
            : Vietnamese;
        Apply(saved == English ? English : Vietnamese, persist: false);
    }

    public void Toggle()
        => Apply(CurrentCulture == English ? Vietnamese : English, persist: true);

    public string Get(string key)
        => Application.Current.TryFindResource(key) as string ?? key;

    public string Format(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private void Apply(string cultureName, bool persist)
    {
        CurrentCulture = cultureName;
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (_activeDictionary is not null)
            Application.Current.Resources.MergedDictionaries.Remove(_activeDictionary);

        _activeDictionary = new ResourceDictionary
        {
            Source = new Uri(
                $"/GH-Net;component/Resources/Languages/Strings.{cultureName}.xaml",
                UriKind.Relative),
        };
        Application.Current.Resources.MergedDictionaries.Add(_activeDictionary);

        if (persist)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferenceFile)!);
            File.WriteAllText(_preferenceFile, cultureName);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
