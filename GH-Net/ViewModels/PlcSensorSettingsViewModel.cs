using GH_Net.Repository.Entities;
using GH_Net.WPF.Localization;
using System.ComponentModel;
using System.Globalization;

namespace GH_Net.WPF.ViewModels;

public sealed class PlcSensorSettingsViewModel(LanguageService language, int number) : ViewModelBase, IDataErrorInfo
{
    private readonly Dictionary<string, string> _values = new()
    {
        [nameof(DelayDistance)] = "0", [nameof(IgnoreDistance)] = "0", [nameof(PulseCount)] = "3600",
        [nameof(EncoderDiameter)] = "51.00", [nameof(Gap)] = "0", [nameof(ErrorCondition)] = "0"
    };
    private readonly HashSet<string> _validated = [];
    public int Number { get; } = number;
    public string Title => language.Format("Plc.SensorTitle", Number);
    public string DelayDistance { get => Get(); set => Set(value); }
    public string IgnoreDistance { get => Get(); set => Set(value); }
    public string PulseCount { get => Get(); set => Set(value); }
    public string EncoderDiameter { get => Get(); set => Set(value); }
    public string Gap { get => Get(); set => Set(value); }
    public string ErrorCondition { get => Get(); set => Set(value); }
    public string Error => string.Join(Environment.NewLine, _values.Keys.Select(k => this[k]).Where(e => e.Length > 0));
    public string this[string field] => !_validated.Contains(field) ? "" : ValidateField(field);
    public string DelayDistanceError => this[nameof(DelayDistance)];
    public string IgnoreDistanceError => this[nameof(IgnoreDistance)];
    public string PulseCountError => this[nameof(PulseCount)];
    public string EncoderDiameterError => this[nameof(EncoderDiameter)];
    public string GapError => this[nameof(Gap)];
    public string ErrorConditionError => this[nameof(ErrorCondition)];

    private string Get([System.Runtime.CompilerServices.CallerMemberName] string field = "") => _values[field];
    private void Set(string value, [System.Runtime.CompilerServices.CallerMemberName] string field = "")
    {
        if (_values[field] == value) return;
        _values[field] = value;
        _validated.Add(field);
        OnPropertyChanged(field); OnPropertyChanged(field + "Error"); OnPropertyChanged(nameof(Error));
    }
    private string ValidateField(string field)
    {
        string raw = _values[field].Trim();
        if (field == nameof(EncoderDiameter))
        {
            if (!decimal.TryParse(raw.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var diameter) ||
                diameter <= 0 || diameter > 999.99m || diameter * 100 != decimal.Truncate(diameter * 100))
                return language.Get("Plc.InvalidDiameter");
        }
        else if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                 value < (field == nameof(PulseCount) ? 1 : 0) || value > 99999)
            return language.Get(field == nameof(PulseCount) ? "Plc.InvalidPulse" : "Plc.InvalidDistance");
        return "";
    }
    public bool Validate()
    {
        foreach (string field in _values.Keys) { _validated.Add(field); OnPropertyChanged(field); OnPropertyChanged(field + "Error"); }
        OnPropertyChanged(nameof(Error));
        return Error.Length == 0;
    }
    public PlcSensorSettings ToEntity()
    {
        if (!Validate()) throw new ArgumentException(Title + ": " + Error);
        int N(string field) => int.Parse(_values[field].Trim(), CultureInfo.InvariantCulture);
        return new PlcSensorSettings { Number = Number, PulseCount = N(nameof(PulseCount)),
            EncoderDiameter = decimal.Parse(EncoderDiameter.Trim().Replace(',', '.'), CultureInfo.InvariantCulture),
            DelayDistance = N(nameof(DelayDistance)), IgnoreDistance = N(nameof(IgnoreDistance)),
            Gap = N(nameof(Gap)), ErrorCondition = N(nameof(ErrorCondition)) };
    }
    public void Load(PlcSensorSettings sensor)
    {
        _values[nameof(PulseCount)] = sensor.PulseCount.ToString(CultureInfo.InvariantCulture);
        _values[nameof(EncoderDiameter)] = sensor.EncoderDiameter.ToString("0.00", CultureInfo.InvariantCulture);
        _values[nameof(DelayDistance)] = sensor.DelayDistance.ToString(CultureInfo.InvariantCulture);
        _values[nameof(IgnoreDistance)] = sensor.IgnoreDistance.ToString(CultureInfo.InvariantCulture);
        _values[nameof(Gap)] = sensor.Gap.ToString(CultureInfo.InvariantCulture);
        _values[nameof(ErrorCondition)] = sensor.ErrorCondition.ToString(CultureInfo.InvariantCulture);
        _validated.Clear(); RefreshLanguage();
    }
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Error));
        foreach (string field in _values.Keys) { OnPropertyChanged(field); OnPropertyChanged(field + "Error"); }
    }
}
